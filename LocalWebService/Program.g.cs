using System;
using System.IO;
using System.Text;
using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Amazon.SecurityToken;
using Amazon.Runtime.CredentialManagement;
using YamlDotNet.RepresentationModel;

public partial class Program
{
    public static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        // AWS credential check: hook first; default hard STS gate when not handled.
        var awsCheckHandled = false;
        ValidateAwsCredentials(host, ref awsCheckHandled);
        if (!awsCheckHandled)
        {
            using (var scope = host.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    var stsClient = services.GetRequiredService<IAmazonSecurityTokenService>();
                    var identity = stsClient.GetCallerIdentityAsync(new GetCallerIdentityRequest()).GetAwaiter().GetResult();
                    Console.WriteLine($"AWS identity: {identity.Arn}");
                }
                catch { throw new Exception("Could not authenticate with AWS"); }
            }
        }
        host.Run();
    }
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();
            })
            .UseContentRoot(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location))
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });

        // Hook: add configuration sources and logging providers (e.g. Serilog, YAML config)
        ConfigureHostBuilder(hostBuilder);

        return hostBuilder;
    }
    public static MemoryStream GenerateStreamFromString(string s)
    {
        var byteArray = Encoding.UTF8.GetBytes(s);
        var stream = new MemoryStream(byteArray);
        return stream;
    }

    // ===== HOST EXTENSION POINTS =====
    // Implement these partial methods in a hand-written Program.cs in the target
    // project (hand-written files survive regeneration).

    /// <summary>
    /// Called after the default host builder is composed. Add configuration
    /// sources (e.g. env-aware YAML config) and logging providers here.
    /// </summary>
    static partial void ConfigureHostBuilder(IHostBuilder builder);

    /// <summary>
    /// Set handled = true to replace (or skip) the default hard STS credential
    /// check performed before the host starts.
    /// </summary>
    static partial void ValidateAwsCredentials(IHost host, ref bool handled);
}
