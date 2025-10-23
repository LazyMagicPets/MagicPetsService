using Amazon.Runtime;
using AwsSignatureVersion4;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace ChatSchemaRepo;

/// <summary>
/// AWS AppSync Events WebSocket publisher implementation
/// </summary>
public class AppSyncWsEventPublisher : IWsEventPublisher
{
    // Configuration option for authentication method
    // TODO: Move this to configuration file once we verify API Key approach works
    private const bool UseApiKeyAuth = true; // Set to false to use IAM/SigV4 signing

    private readonly ILogger<AppSyncWsEventPublisher> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _region;

    private readonly AwsCredentialsCache _credentialsCache;

    public AppSyncWsEventPublisher(
        AwsCredentialsCache credentialsCache,
        ILogger<AppSyncWsEventPublisher> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _credentialsCache = credentialsCache;
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;

        // Region resolution with multiple fallbacks
        _region = _configuration["AWS_REGION"]
            ?? _configuration["AWS:Region"]
            ?? "us-east-1";

        _logger.LogInformation("AppSync WebSocket Publisher initialized. Region: {Region}. EventsApi will be resolved dynamically based on caller authentication.", _region);
    }

    /// <summary>
    /// Resolves EventsApi configuration dynamically based on authenticator name.
    /// Uses convention: {authname}EventsApi (e.g., tenantauth -> tenantauthEventsApi)
    /// </summary>
    private (string? httpDomain, string? apiKey) ResolveEventsApiConfig(string? authName)
    {
        if (string.IsNullOrEmpty(authName))
        {
            _logger.LogWarning("No authName provided in CallerInfo. Cannot resolve EventsApi configuration. Events will not be published.");
            return (null, null);
        }

        // Convention-based mapping: {authname}EventsApi
        var configKey = $"AWS:AppSync:{authName}EventsApi";
        var httpDomain = _configuration[$"{configKey}:HttpDomain"];
        var apiKey = _configuration[$"{configKey}:ApiKey"];

        if (!string.IsNullOrEmpty(httpDomain))
        {
            _logger.LogDebug("Resolved EventsApi for authName '{AuthName}': {HttpDomain}", authName, httpDomain);
        }
        else
        {
            _logger.LogWarning("No EventsApi configuration found for authName '{AuthName}'. " +
                "Expected configuration key: {ConfigKey}:HttpDomain. Events will not be published.",
                authName, configKey);
        }

        if (string.IsNullOrEmpty(apiKey) && UseApiKeyAuth)
        {
            _logger.LogWarning("No API Key found for authName '{AuthName}'. " +
                "Expected configuration key: {ConfigKey}:ApiKey. Events may fail to publish.",
                authName, configKey);
        }

        return (httpDomain, apiKey);
    }

    public async Task PublishAsync<T>(
        string channel,
        string eventType,
        T data,
        Dictionary<string, object>? metadata = null,
        ICallerInfo? callerInfo = null)
    {
        try
        {
            // Extract dataType from metadata or infer from T
            var dataTypeName = metadata?.GetValueOrDefault("dataType")?.ToString()
                ?? GetDataTypeName<T>();

            // Extract chatId from channel path
            var chatId = ExtractChatIdFromChannel(channel);

            // Build event payload with AppSync Events structure
            var eventPayload = new
            {
                chatId = chatId,
                eventType = eventType,
                timestamp = DateTime.UtcNow.ToString("O"),
                data = data,
                dataType = dataTypeName
            };

            _logger.LogInformation("Publishing event: {EventType} to channel: {Channel} with data type: {DataType}",
                eventType, channel, dataTypeName);

            // Resolve EventsApi configuration dynamically based on caller's authName
            var (httpDomain, apiKey) = ResolveEventsApiConfig(callerInfo?.Authname);

            // Check configuration
            if (string.IsNullOrEmpty(httpDomain))
            {
                _logger.LogWarning("AppSync Event API HTTP Domain not resolved, logging event instead");
                _logger.LogDebug("Event payload: {Payload}", JsonSerializer.Serialize(eventPayload));
                return;
            }

            // Publish to AppSync Events via HTTP
            await PublishEventAsync(channel, eventPayload, eventType, httpDomain, apiKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event: {EventType} to channel: {Channel}", eventType, channel);
            // Don't throw - event publishing should be non-blocking
        }
    }

    private string GetDataTypeName<T>()
    {
        var type = typeof(T);

        // For anonymous types, use a more friendly name
        if (type.Name.Contains("AnonymousType") || type.Name.Contains("<>"))
        {
            return "Object";
        }

        // Use the actual type name (e.g., "ChatMessage")
        return type.Name;
    }

    private string ExtractChatIdFromChannel(string channel)
    {
        // Extract chatId from "/chat/{chatId}" pattern
        var parts = channel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    private async Task PublishEventAsync(string channel, object eventPayload, string eventType, string httpDomain, string? apiKey)
    {
        // AWS AppSync Events API expects the events array to contain JSON STRINGS, not objects
        // So we serialize the event to a JSON string, then serialize that string again as part of the array
        var eventJsonString = JsonSerializer.Serialize(eventPayload);

        // Build the request body with events array containing JSON strings
        var requestBody = new
        {
            channel = channel,
            events = new[] { eventJsonString }
        };

        var requestJson = JsonSerializer.Serialize(requestBody);

        var httpClient = _httpClientFactory.CreateClient("AppSyncEvents");
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        if (UseApiKeyAuth)
        {
            // API Key authentication - simpler, no signing required
            httpClient.DefaultRequestHeaders.Clear();

            // Use the resolved API key
            if (!string.IsNullOrEmpty(apiKey))
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
                _logger.LogDebug("  API Key configured: {ApiKeyPrefix}...", apiKey.Length > 8 ? apiKey.Substring(0, 8) : "***");
            }
            else
            {
                _logger.LogWarning("API Key not provided for AppSync Events API");
            }

            // Use standard JSON content type
            content.Headers.Remove("Content-Type");
            content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        }
        else
        {
            // IAM/SigV4 authentication - requires signing
            // Get credentials from the cache (resolved once at startup from AddDefaultAWSOptions)
            var credentials = _credentialsCache.GetCredentials();
            var immutableCredentials = await credentials.GetCredentialsAsync();

            // Create ImmutableCredentials for the signer
            var signingCredentials = new ImmutableCredentials(
                immutableCredentials.AccessKey,
                immutableCredentials.SecretKey,
                immutableCredentials.Token);

            // Add required AppSync Events API headers to HttpClient.DefaultRequestHeaders
            // These will be included in the SigV4 signature calculation
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("accept", "application/json, text/javascript");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("content-encoding", "amz-1.0");

            // Update Content-Type to include charset as required by AppSync Events API
            content.Headers.Remove("Content-Type");
            content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=UTF-8");
        }

        // Log detailed request information for debugging
        var url = $"https://{httpDomain}/event";
        _logger.LogDebug("AppSync Events API Request Details:");
        _logger.LogDebug("  Auth Method: {AuthMethod}", UseApiKeyAuth ? "API Key" : "IAM/SigV4");
        _logger.LogDebug("  URL: {Url}", url);
        _logger.LogDebug("  Region: {Region}", _region);
        _logger.LogDebug("  Channel: {Channel}", channel);
        _logger.LogDebug("  Request Body: {RequestBody}", requestJson);

        // Make the HTTP request with appropriate authentication
        HttpResponseMessage response;
        if (UseApiKeyAuth)
        {
            // Simple HTTP POST with API Key in header
            response = await httpClient.PostAsync(url, content);
        }
        else
        {
            // SigV4 signed request
            var credentials = _credentialsCache.GetCredentials();
            var immutableCredentials = await credentials.GetCredentialsAsync();
            var signingCredentials = new ImmutableCredentials(
                immutableCredentials.AccessKey,
                immutableCredentials.SecretKey,
                immutableCredentials.Token);

            response = await httpClient.PostAsync(
                url,
                content,
                regionName: _region,
                serviceName: "appsync",
                credentials: signingCredentials);
        }

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Successfully published event to AppSync Events API: {EventType} for channel: {Channel}",
                eventType, channel);
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to publish event to AppSync Events API. Status: {StatusCode}, Error: {Error}",
                response.StatusCode, errorContent);
        }
    }
}
