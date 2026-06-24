using Amazon.Lambda.APIGatewayEvents;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LambdaFunc;

/// <summary>
/// This class extends from APIGatewayProxyFunction which contains the method FunctionHandlerAsync which is the 
/// actual Lambda function entry point. The Lambda handler field should be set to
/// 
/// PetStoreOrderFunc::PetStoreOrderFunc.LambdaEntryPoint::FunctionHandlerAsync
/// </summary>
public partial class LambdaEntryPoint :
    // The base class must be set to match the AWS service invoking the Lambda function. If not Amazon.Lambda.AspNetCoreServer
    // will fail to convert the incoming request correctly into a valid ASP.NET Core request.
    //
    // API Gateway REST API                         -> Amazon.Lambda.AspNetCoreServer.APIGatewayProxyFunction
    // API Gateway HTTP API payload version 1.0     -> Amazon.Lambda.AspNetCoreServer.APIGatewayProxyFunction
    // API Gateway HTTP API payload version 2.0     -> Amazon.Lambda.AspNetCoreServer.APIGatewayHttpApiV2ProxyFunction
    // Application Load Balancer                    -> Amazon.Lambda.AspNetCoreServer.ApplicationLoadBalancerFunction
    // 
    // Note: When using the AWS::Serverless::Function resource with an event type of "HttpApi" then payload version 2.0
    // will be the default and you must make Amazon.Lambda.AspNetCoreServer.APIGatewayHttpApiV2ProxyFunction the base class.
    Amazon.Lambda.AspNetCoreServer.APIGatewayHttpApiV2ProxyFunction
{
    /// <summary>
    /// The builder has configuration, logging and Amazon API Gateway already configured. The startup class
    /// needs to be configured in this method using the UseStartup<>() method.
    /// </summary>
    /// <param name="builder"></param>
    protected override void Init(IWebHostBuilder builder)
    {
        builder
           .UseStartup<Startup>()
           .ConfigureServices(services =>
           {
               services.AddSingleton<IStartupFilter, CognitoHeadersStartupFilter>();
           });
    }

    private class CognitoHeadersStartupFilter : IStartupFilter
    {
        private static readonly Regex IssuerPattern = new(@"https://cognito-idp\.(.*?)\.amazonaws\.com/(.*?)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private const string RegionHeader = "lz-cognito-region";
        private const string UserPoolHeader = "lz-cognito-userpool-id";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            Console.WriteLine("Adding Cognito headers Filter to request pipeline");
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    // Add headers to the request that identify the Cognito region and user pool ID
                    // This information is extracted from the 'iss' claim in the JWT token provided by Cognito.

                    try
                    {
                        // Check if the user is authenticated and retrieve the 'iss' claim from the JWT
                        if (context.User?.Identity?.IsAuthenticated == true)
                        {
                            var issuer = context.User.Claims.FirstOrDefault(c => c.Type == "iss")?.Value;
                            if (!string.IsNullOrEmpty(issuer))
                            {
                                var match = IssuerPattern.Match(issuer);
                                if (match.Success && match.Groups.Count >= 3)
                                {
                                    var region = match.Groups[1].Value;
                                    var userPoolId = match.Groups[2].Value;

                                    if (!string.IsNullOrEmpty(region) && !string.IsNullOrEmpty(userPoolId))
                                    {
                                        Console.WriteLine($"Setting headers {RegionHeader}={region} and {UserPoolHeader}={userPoolId}");
                                        context.Request.Headers.TryAdd(RegionHeader, region);
                                        context.Request.Headers.TryAdd(UserPoolHeader, userPoolId);
                                    }
                                }
                            }
                        }
                        else
                        {
                            context.Request.Headers.TryAdd(RegionHeader, "");
                            context.Request.Headers.TryAdd(UserPoolHeader, "");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error adding Cognito headers: {ex.Message}");
                        // Consider using a proper logging framework instead of Console.WriteLine for production scenarios.
                    }

                    await nextMiddleware();
                });

                next(app);
            };
        }
    }
}
