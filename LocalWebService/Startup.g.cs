using System.Linq;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.CloudFront;
using Amazon.CloudFrontKeyValueStore;
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
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
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

        // AWS credentials/services: hook first; default systemconfig.yaml
        // (Profile/Region) discovery when not handled.
        var awsHandled = false;
        ConfigureAwsServices(services, ref awsHandled);
        if (!awsHandled)
        {
            // Configure AWS credentials from the local systemconfig.yaml (Profile/Region).
            try
            {
                using (var reader = new StreamReader("../../systemconfig.yaml"))
                {
                    var yaml = new YamlStream();
                    yaml.Load(reader);

                    var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;

                    string profile = null;
                    string region = "us-east-1";

                    if (mapping.Children.TryGetValue(new YamlScalarNode("Profile"), out var profileNode))
                    {
                        profile = ((YamlScalarNode)profileNode).Value;
                        logger.LogInformation($"Using AWS Profile: {profile}");
                    }
                    else
                        throw new Exception("Profile not found in systemconfig.yaml");

                    if (mapping.Children.TryGetValue(new YamlScalarNode("Region"), out var regionNode))
                        region = ((YamlScalarNode)regionNode).Value;

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
                        services.AddDefaultAWSOptions(options);
                    }

                    if (awsCredentials != null)
                    {
                        services.AddSingleton(awsCredentials);
                    }

                    // AWS services used by the local routing middleware and LzAuthorization.
                    services.AddAWSService<Amazon.DynamoDBv2.IAmazonDynamoDB>();
                    services.AddAWSService<IAmazonSecurityTokenService>();
                    services.AddAWSService<IAmazonCloudFrontKeyValueStore>();
                    services.AddAWSService<IAmazonCloudFront>();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to configure AWS credentials");
                throw;
            }
        }

        // Default permissive CORS for local development. Hosts may register a
        // different default policy in ConfigureHostServices (later registration wins).
        services.AddCors(opt =>
        {
            opt.AddDefaultPolicy(builder =>
            {
                builder.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        // Hook for host-specific service registrations (before modules are registered)
        ConfigureHostServices(services);

        ConfigureSvcs(services);

        services
            .AddControllers()
            .AddNewtonsoftJson();
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

        // Pipeline: hook first; default AWS local routing pipeline when not handled.
        var pipelineHandled = false;
        ConfigurePipeline(app, env, ref pipelineHandled);
        if (pipelineHandled)
            return;

        // UseAwsLocalWebApiRoutingMiddleware mimics the CloudFront {systemKey}---request function
        // for local debugging: it reads the local systemconfig.yaml and the system's CloudFront
        // KeyValueStore ({systemKey}---kvs) and adds the headers the LzAuthorization middleware needs.
        // See https://github.com/LazyMagicOrg/LazyMagic
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

    // ===== HOST EXTENSION POINTS =====
    // Implement any of these partial methods in a hand-written Startup.cs in the
    // target project (hand-written files survive regeneration). Hooks with a
    // 'ref bool handled' parameter replace the corresponding default block when
    // the implementation sets handled = true.

    /// <summary>
    /// Override in the host's Startup.cs to register implementation-specific services.
    /// Called before modules are registered, allowing modules to depend on interfaces
    /// that are implemented by services registered here.
    /// </summary>
    partial void ConfigureHostServices(IServiceCollection services);

    /// <summary>
    /// Set handled = true to replace the default systemconfig.yaml AWS
    /// credential/service setup.
    /// </summary>
    partial void ConfigureAwsServices(IServiceCollection services, ref bool handled);

    /// <summary>
    /// Set handled = true to replace the default HTTP request pipeline.
    /// </summary>
    partial void ConfigurePipeline(IApplicationBuilder app, IWebHostEnvironment env, ref bool handled);
}
