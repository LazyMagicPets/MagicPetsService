using System.Linq;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.CloudFront;
using Amazon.CloudFrontKeyValueStore;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LazyMagic.Shared;
using LazyMagic.Service.AwsLocalWebApiRoutingMiddleware;
using YamlDotNet.RepresentationModel;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;

public partial class Startup
{
    private readonly IConfiguration _configuration;

    public Startup(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        var logger = services.BuildServiceProvider()
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<Startup>();

        // Configure AWS credentials FIRST
        try 
        {
            using (var reader = new StreamReader("../../systemconfig.yaml"))
            {
                var yaml = new YamlStream();
                yaml.Load(reader);

                var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;
                
                string profile = null;
                string region = null;

                if (mapping.Children.TryGetValue(new YamlScalarNode("Profile"), out var profileNode))
                {
                    profile = ((YamlScalarNode)profileNode).Value;
                    logger.LogInformation($"Using AWS Profile: {profile}");
                }
                else 
                    throw new Exception("Profile not found in systemconfig.yaml");

                // Default to us-east-1 if Region not specified
                region = "us-east-1";
                if (mapping.Children.TryGetValue(new YamlScalarNode("Region"), out var regionNode))
                    region = ((YamlScalarNode)regionNode).Value;

                // Store region in configuration for services that need it
                _configuration["AWS:Region"] = region;
                logger.LogInformation($"Using AWS Region: {region}");

                var options = new AWSOptions
                {
                    Profile = profile,
                    Region = Amazon.RegionEndpoint.GetBySystemName(region)
                };

                var chain = new CredentialProfileStoreChain();
                AWSCredentials awsCredentials = null;
                if (chain.TryGetAWSCredentials(profile, out awsCredentials))
                {
                    options.Credentials = awsCredentials;
                    services.AddDefaultAWSOptions(options);  // Move this up BEFORE any AWS services
                }

                // Register AWS credentials as a singleton for services that need raw AWS API calls
                // This ensures the same credentials used by all AWS SDK clients are available
                if (awsCredentials != null)
                {
                    services.AddSingleton(awsCredentials);
                }

                // Now register AWS services
                services.AddAWSService<Amazon.DynamoDBv2.IAmazonDynamoDB>();
                services.AddAWSService<IAmazonSecurityTokenService>();
                services.AddAWSService<IAmazonCloudFrontKeyValueStore>();
                services.AddAWSService<IAmazonCloudFront>();
                services.AddAWSService<IAmazonCloudFormation>();

                // Get stack name from systemconfig.yaml and retrieve AppSync Events API URLs
                string systemKey = null;

                if (mapping.Children.TryGetValue(new YamlScalarNode("SystemKey"), out var systemKeyNode))
                    systemKey = ((YamlScalarNode)systemKeyNode).Value;

                // Get the Events API type from environment variable (Tenant or Consumer)
                var eventsApiType = Environment.GetEnvironmentVariable("APPSYNC_EVENTS_API_TYPE") ?? "Tenant";
                logger.LogInformation($"Using AppSync Events API Type: {eventsApiType}");

                if (!string.IsNullOrEmpty(systemKey))
                {
                    var stackName = $"{systemKey}---service";
                    logger.LogInformation($"Retrieving AppSync Events API URLs from stack: {stackName}");

                    try
                    {
                        var tempProvider = services.BuildServiceProvider();
                        var cfnClient = tempProvider.GetRequiredService<IAmazonCloudFormation>();

                        var describeStacksRequest = new DescribeStacksRequest
                        {
                            StackName = stackName
                        };

                        var describeStacksResponse = cfnClient.DescribeStacksAsync(describeStacksRequest).GetAwaiter().GetResult();
                        var stack = describeStacksResponse.Stacks.FirstOrDefault();

                        if (stack != null)
                        {
                            // Find all outputs ending with "EventsApiHttpDomain"
                            var eventsApiOutputs = stack.Outputs.Where(o => o.OutputKey.EndsWith("EventsApiHttpDomain")).ToList();

                            foreach (var output in eventsApiOutputs)
                            {
                                logger.LogInformation($"Found {output.OutputKey}: {output.OutputValue}");

                                // Extract the API name (e.g., "Tenant" from "TenantEventsApiHttpDomain")
                                var apiName = output.OutputKey.Replace("EventsApiHttpDomain", "");

                                // Store in configuration
                                _configuration[$"AWS:AppSync:{apiName}EventsApi:HttpDomain"] = output.OutputValue;

                                // If this matches the requested API type, also set it as the default
                                if (apiName.Equals(eventsApiType, StringComparison.OrdinalIgnoreCase))
                                {
                                    logger.LogInformation($"Setting {apiName}EventsApi as the active Events API");

                                    // Also set unified configuration for forward compatibility
                                    _configuration["AWS:AppSync:EventsApi:HttpDomain"] = output.OutputValue;
                                }
                            }

                            if (!eventsApiOutputs.Any())
                            {
                                logger.LogWarning("No EventsApiHttpDomain outputs found in stack");
                            }

                            // Retrieve API Keys from stack outputs
                            var apiKeyOutputs = stack.Outputs.Where(o => o.OutputKey.EndsWith("EventsApiApiKey")).ToList();

                            foreach (var output in apiKeyOutputs)
                            {
                                logger.LogInformation($"Found {output.OutputKey}: {output.OutputValue}");

                                // Extract the API name (e.g., "Tenant" from "TenantEventsApiApiKey")
                                var apiName = output.OutputKey.Replace("EventsApiApiKey", "");

                                // Store in configuration
                                _configuration[$"AWS:AppSync:{apiName}EventsApi:ApiKey"] = output.OutputValue;

                                // If this matches the active API type, also set unified configuration
                                if (apiName.Equals(eventsApiType, StringComparison.OrdinalIgnoreCase))
                                {
                                    _configuration["AWS:AppSync:EventsApi:ApiKey"] = output.OutputValue;
                                }
                            }

                            if (!apiKeyOutputs.Any())
                            {
                                logger.LogWarning("No EventsApiApiKey outputs found in stack");
                            }
                        }
                        else
                        {
                            logger.LogWarning($"Stack {stackName} not found");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, $"Failed to retrieve AppSync Events API URLs from stack {stackName}. Events will be logged instead of published.");
                    }
                }

                // Then configure other services
                ConfigureSvcs(services);

                services.AddCors(opt =>
                {
                    opt.AddDefaultPolicy(builder =>
                    {
                        builder.AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
                });

                services
                    .AddControllers()
                    .AddNewtonsoftJson();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to configure AWS credentials");
            throw;
        }
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {

        if (env.IsDevelopment())
        {
            var actionDescriptorCollectionProvider = app.ApplicationServices.GetRequiredService<IActionDescriptorCollectionProvider>();
            var routes = actionDescriptorCollectionProvider.ActionDescriptors.Items
                .Where(ad => ad is ControllerActionDescriptor)
                .Cast<ControllerActionDescriptor>()
                .Select(ad => new
                {
                    Controller = ad.ControllerName,
                    Action = ad.ActionName,
                    Method = ad.ActionConstraints?.OfType<HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.First(),
                    Route = ad.AttributeRouteInfo?.Template ?? "No route template"
                });

            var logger = app.ApplicationServices.GetRequiredService<ILogger<Startup>>();
            foreach (var route in routes)
            {
                logger.LogInformation($"Endpoint: {route.Method} {route.Route} -> {route.Controller}.{route.Action}");
            }
        }

        // UseAwsLocalWebApiRoutingMiddleware is a LazyMagic middleware that handles routing based on
        // your systemconfig.yaml and AWS CloudFront Key-Value Store configuration.
        // It is only useful in a local web service environment where the systemconfig.yaml
        // file can be read. This routine is used to mimic the behavior of the CloudFront 
        // {systemKey}---request function, which adds headers to the request required by 
        // the LzAuthorization middleware.
        // 1. Reads the local systemconfig.yaml file to get the systemKey and defaultTenancy.
        // 2. Reads the system's CloudFront KeyValueStore named {systemKey}---kvs to get the ARN of the KeyValueStore. 
        // 3. Loads the _defaultTenancy config, which is subsequently used to set headers for each Api request.
        // See the LazyMagic.Service.AwsLocalWebApiRoutingMiddleware
        // project for more details. https://github.com/LazyMagicOrg/LazyMagic
        app.UseAwsLocalWebApiRoutingMiddleware(); 
        app.UseRouting();
        app.UseCors();
        app.UseWebSockets(new WebSocketOptions()
        {
            KeepAliveInterval = TimeSpan.FromSeconds(120)
        });
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });

    }
}