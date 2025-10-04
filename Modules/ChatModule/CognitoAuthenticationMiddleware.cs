using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace ChatModule;

/// <summary>
/// Middleware for validating Cognito JWT tokens in App Runner environment.
/// Since App Runner doesn't provide built-in Cognito authentication like API Gateway,
/// we must implement JWT validation within the application.
/// </summary>
public class CognitoAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CognitoAuthenticationMiddleware> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _userPoolId;
    private readonly string _region;
    private readonly string _jwksUri;
    private JsonWebKeySet? _cachedJwks;
    private DateTime _jwksCacheExpiry = DateTime.MinValue;
    private readonly TimeSpan _jwksCacheDuration = TimeSpan.FromHours(1);

    public CognitoAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<CognitoAuthenticationMiddleware> logger,
        IConfiguration configuration,
        HttpClient httpClient)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;

        _userPoolId = _configuration["AWS:Cognito:UserPoolId"] ?? throw new InvalidOperationException("AWS:Cognito:UserPoolId not configured");
        _region = _configuration["AWS:Region"] ?? throw new InvalidOperationException("AWS:Region not configured");
        _jwksUri = $"https://cognito-idp.{_region}.amazonaws.com/{_userPoolId}/.well-known/jwks.json";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for health check and public endpoints
        if (IsPublicEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        try
        {
            var token = ExtractTokenFromHeader(context);

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("No authorization token provided for {Path}", context.Request.Path);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Authorization token required");
                return;
            }

            var claimsPrincipal = await ValidateTokenAsync(token);
            if (claimsPrincipal == null)
            {
                _logger.LogWarning("Invalid token provided for {Path}", context.Request.Path);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid authorization token");
                return;
            }

            // Set the user context for the request
            context.User = claimsPrincipal;
            _logger.LogDebug("Authenticated user {UserId} for {Path}", claimsPrincipal.Identity?.Name, context.Request.Path);

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error for {Path}", context.Request.Path);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Authentication service error");
        }
    }

    private bool IsPublicEndpoint(PathString path)
    {
        var publicPaths = new[]
        {
            "/health",
            "/ready",
            "/metrics"
        };

        return publicPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
    }

    private string? ExtractTokenFromHeader(HttpContext context)
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader))
            return null;

        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader.Substring("Bearer ".Length).Trim();

        return null;
    }

    private async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        try
        {
            // Parse the JWT header to get the key ID (kid)
            var tokenHandler = new JwtSecurityTokenHandler();
            var jsonToken = tokenHandler.ReadJwtToken(token);

            var kid = jsonToken.Header.Kid;
            if (string.IsNullOrEmpty(kid))
            {
                _logger.LogWarning("JWT token missing 'kid' in header");
                return null;
            }

            // Get the signing key from JWKS
            var signingKey = await GetSigningKeyAsync(kid);
            if (signingKey == null)
            {
                _logger.LogWarning("No matching signing key found for kid: {Kid}", kid);
                return null;
            }

            // Validate the token
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = signingKey,
                ValidateIssuer = true,
                ValidIssuer = $"https://cognito-idp.{_region}.amazonaws.com/{_userPoolId}",
                ValidateAudience = false, // Cognito tokens may not have aud claim set for user pool tokens
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

            // Additional Cognito-specific validation
            if (validatedToken is JwtSecurityToken jwt)
            {
                // Verify token_use claim
                var tokenUse = jwt.Claims.FirstOrDefault(c => c.Type == "token_use")?.Value;
                if (tokenUse != "access" && tokenUse != "id")
                {
                    _logger.LogWarning("Invalid token_use: {TokenUse}", tokenUse);
                    return null;
                }

                _logger.LogDebug("Successfully validated Cognito JWT token");
                return principal;
            }

            return null;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("JWT token has expired");
            return null;
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("JWT token has invalid signature");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating JWT token");
            return null;
        }
    }

    private async Task<SecurityKey?> GetSigningKeyAsync(string kid)
    {
        try
        {
            // Check if we need to refresh the JWKS cache
            if (_cachedJwks == null || DateTime.UtcNow > _jwksCacheExpiry)
            {
                _logger.LogDebug("Fetching JWKS from {JwksUri}", _jwksUri);
                var jwksJson = await _httpClient.GetStringAsync(_jwksUri);
                _cachedJwks = new JsonWebKeySet(jwksJson);
                _jwksCacheExpiry = DateTime.UtcNow.Add(_jwksCacheDuration);
                _logger.LogDebug("JWKS cache updated, expires at {Expiry}", _jwksCacheExpiry);
            }

            // Find the key with matching kid
            var key = _cachedJwks.Keys.FirstOrDefault(k => k.Kid == kid);
            if (key == null)
            {
                _logger.LogWarning("No key found with kid: {Kid}", kid);
                return null;
            }

            // Convert to SecurityKey
            if (key.Kty == "RSA")
            {
                var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Base64UrlEncoder.DecodeBytes(key.N),
                    Exponent = Base64UrlEncoder.DecodeBytes(key.E)
                });
                return new RsaSecurityKey(rsa) { KeyId = key.Kid };
            }

            _logger.LogWarning("Unsupported key type: {KeyType}", key.Kty);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting signing key for kid: {Kid}", kid);
            return null;
        }
    }
}

/// <summary>
/// Extension methods for configuring Cognito authentication middleware
/// </summary>
public static class CognitoMiddlewareExtensions
{
    public static IServiceCollection AddCognitoMiddleware(this IServiceCollection services)
    {
        // Register HttpClient for JWKS fetching
        services.AddHttpClient<CognitoAuthenticationMiddleware>();

        return services;
    }

    public static IApplicationBuilder UseCognitoAuthentication(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CognitoAuthenticationMiddleware>();
    }
}