using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using ChatModule;
using ChatSchema;
using ChatSchemaRepo;
using LazyMagic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YamlDotNet.RepresentationModel;

namespace TestModules;

/// <summary>
/// Test fixture for ChatModule that sets up dependency injection
/// and provides access to the controller for testing.
/// </summary>
public class ChatModuleTestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; }
    public IChatModuleController Controller { get; }
    public ICallerInfo CallerInfo { get; }
    public MockAppSyncEventPublisher EventPublisher { get; }

    public ChatModuleTestFixture()
    {
        // Setup dependency injection
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        // Configure AWS credentials from systemconfig.yaml (same pattern as LocalWebService)
        try
        {
            var tempLogger = services.BuildServiceProvider()
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<ChatModuleTestFixture>();
            var systemConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..","..","..","..", "systemconfig.yaml");

            if (!File.Exists(systemConfigPath))
            {
                throw new Exception($"systemconfig.yaml not found at {systemConfigPath}");
            }

            using (var reader = new StreamReader(systemConfigPath))
            {
                var yaml = new YamlStream();
                yaml.Load(reader);

                var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;

                string? profile = null;
                string region = "us-east-1"; // Default

                if (mapping.Children.TryGetValue(new YamlScalarNode("Profile"), out var profileNode))
                {
                    profile = ((YamlScalarNode)profileNode).Value;
                    tempLogger.LogInformation($"Using AWS Profile: {profile}");
                }
                else
                {
                    throw new Exception("Profile not found in systemconfig.yaml");
                }

                if (mapping.Children.TryGetValue(new YamlScalarNode("Region"), out var regionNode))
                {
                    region = ((YamlScalarNode)regionNode).Value!;
                }

                var options = new AWSOptions
                {
                    Profile = profile,
                    Region = Amazon.RegionEndpoint.GetBySystemName(region)
                };

                // Log available profiles for debugging
                tempLogger.LogInformation("Attempting to load AWS credentials...");
                tempLogger.LogInformation($"Profile requested: {profile}");
                tempLogger.LogInformation($"Region: {region}");

                var chain = new CredentialProfileStoreChain();
                bool credentialsLoaded = false;

                try
                {
                    if (chain.TryGetAWSCredentials(profile, out var credentials))
                    {
                        tempLogger.LogInformation($"Successfully loaded credentials for profile: {profile}");
                        options.Credentials = credentials;
                        credentialsLoaded = true;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    tempLogger.LogWarning($"SSO profile '{profile}' requires interactive login. Error: {ex.Message}");
                    tempLogger.LogInformation("Falling back to default credential chain (environment variables, EC2 instance profile, etc.)");
                }

                if (credentialsLoaded)
                {
                    services.AddDefaultAWSOptions(options);
                }
                else
                {
                    // Use FallbackCredentialsFactory which will try:
                    // 1. Environment variables (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY)
                    // 2. Default profile in credentials file
                    // 3. EC2 instance profile
                    // 4. ECS container credentials
                    tempLogger.LogInformation("Using fallback credentials (FallbackCredentialsFactory)");
                    options.Credentials = Amazon.Runtime.FallbackCredentialsFactory.GetCredentials();
                    services.AddDefaultAWSOptions(options);
                }

                // Register AWS services
                services.AddAWSService<IAmazonDynamoDB>();
                services.AddAWSService<IAmazonSecurityTokenService>();

                // Verify AWS credentials work
                var tempProvider = services.BuildServiceProvider();
                var stsClient = tempProvider.GetRequiredService<IAmazonSecurityTokenService>();
                var identity = stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest()).GetAwaiter().GetResult();
                tempLogger.LogInformation($"AWS identity verified: {identity.Arn}");
            }
        }
        catch (Exception ex)
        {
            var errorLogger = services.BuildServiceProvider()
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<ChatModuleTestFixture>();
            errorLogger.LogError(ex, "Failed to configure AWS credentials");
            throw;
        }

        // Create configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Create mock caller info
        CallerInfo = new MockCallerInfo();

        // Create and register mock event publisher BEFORE calling AddChatModule
        EventPublisher = new MockAppSyncEventPublisher();
        services.AddSingleton<IAppSyncEventPublisher>(EventPublisher);

        // Register mock authorization BEFORE calling AddChatModule
        // This ensures our mock authorization will be used instead of the default
        services.AddSingleton<IChatModuleAuthorization>(new MockChatModuleAuthorization(CallerInfo));

        // Register ChatModule services using the generated registration method
        // Note: ChatModuleRegistration uses TryAddSingleton, so our mock above takes precedence
        services.AddChatModule();

        // Build service provider
        ServiceProvider = services.BuildServiceProvider();

        // Start the ChatManagerService (IHostedService) manually for tests
        var chatManagerService = ServiceProvider.GetRequiredService<IChatManagerService>() as ChatManagerService;
        if (chatManagerService != null)
        {
            chatManagerService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        // Get controller instance
        Controller = ServiceProvider.GetRequiredService<IChatModuleController>();
    }

    public void Dispose()
    {
        // Stop the ChatManagerService before disposing
        var chatManagerService = ServiceProvider.GetService<IChatManagerService>() as ChatManagerService;
        if (chatManagerService != null)
        {
            chatManagerService.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
