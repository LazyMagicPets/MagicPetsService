using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace ChatModule;

public static class CognitoAuthenticationExtensions
{
    public static IServiceCollection ConfigureCognitoAuthentication(this IServiceCollection services)
    {
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // Get configuration from environment variables
            var cognitoRegion = Environment.GetEnvironmentVariable("COGNITO_REGION") ?? "us-west-2";
            var userPoolId = Environment.GetEnvironmentVariable("COGNITO_USER_POOL_ID");

            if (string.IsNullOrEmpty(userPoolId))
            {
                throw new InvalidOperationException("COGNITO_USER_POOL_ID environment variable is required");
            }

            // Configure JWT validation for Cognito tokens
            options.Authority = $"https://cognito-idp.{cognitoRegion}.amazonaws.com/{userPoolId}";
            options.RequireHttpsMetadata = true;
            options.SaveToken = true;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"https://cognito-idp.{cognitoRegion}.amazonaws.com/{userPoolId}",
                ValidateAudience = false, // Cognito tokens don't typically include audience
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                    logger.LogError(context.Exception, "JWT authentication failed");
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                    logger.LogDebug("JWT token validated successfully for user: {User}",
                        context.Principal?.Identity?.Name);
                    return Task.CompletedTask;
                }
            };
        });

        return services;
    }
}