using Amazon.Runtime;
using AwsSignatureVersion4;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace ChatSchemaRepo;

public class AppSyncEventPublisher : IAppSyncEventPublisher
{
    // Configuration option for authentication method
    // TODO: Move this to configuration file once we verify API Key approach works
    private const bool UseApiKeyAuth = true; // Set to false to use IAM/SigV4 signing

    private readonly ILogger<AppSyncEventPublisher> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _region;
    private readonly string? _tenantEventApiHttpDomain;
    private readonly string? _consumerEventApiHttpDomain;
    private readonly string? _tenantEventApiKey;
    private readonly string? _consumerEventApiKey;

    private readonly AwsCredentialsCache _credentialsCache;

    public AppSyncEventPublisher(
        AwsCredentialsCache credentialsCache,
        ILogger<AppSyncEventPublisher> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _credentialsCache = credentialsCache;
        _logger = logger;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;

        // Read region and API domains from configuration/environment variables
        // These work in both Lambda (from CloudFormation env vars) and LocalWebService (from appsettings/env)
        _region = _configuration["AWS_REGION"] ?? _configuration["AWS:Region"] ?? "us-east-1";
        _tenantEventApiHttpDomain = _configuration["AWS:AppSync:TenantEventsApi:HttpDomain"];
        _consumerEventApiHttpDomain = _configuration["AWS:AppSync:ConsumerEventsApi:HttpDomain"];
        _tenantEventApiKey = _configuration["AWS:AppSync:TenantEventsApi:ApiKey"];
        _consumerEventApiKey = _configuration["AWS:AppSync:ConsumerEventsApi:ApiKey"];
    }

    public async Task PublishChatEventAsync(string chatId, ChatEvent sessionEvent)
    {
        try
        {
            // Determine the data type name for the client BEFORE serialization
            string? dataTypeName = null;
            if (sessionEvent.Data != null)
            {
                var dataType = sessionEvent.Data.GetType();

                // For anonymous types, use a more friendly name
                if (dataType.Name.Contains("AnonymousType") || dataType.Name.Contains("<>"))
                {
                    dataTypeName = "Object";
                }
                else
                {
                    // Use the actual type name (e.g., "ChatMessage")
                    dataTypeName = dataType.Name;
                }
            }

            // Serialize the data first to preserve it properly
            // Then wrap it with metadata
            var eventPayload = new
            {
                chatId = sessionEvent.ChatId,
                eventType = sessionEvent.EventType.ToString(),
                timestamp = sessionEvent.Timestamp.ToString("O"),
                data = sessionEvent.Data,  // Will be serialized with the payload
                dataType = dataTypeName
            };

            _logger.LogInformation("Publishing session event: {EventType} for session: {SessionId} with data type: {DataType}",
                sessionEvent.EventType, chatId, dataTypeName ?? "null");

            // Use TenantEventsApi for chat events (adjust if different logic needed)
            var eventApiHttpDomain = _tenantEventApiHttpDomain;

            if (string.IsNullOrEmpty(eventApiHttpDomain))
            {
                _logger.LogWarning("AppSync Event API HTTP Domain not configured, logging event instead");
                _logger.LogDebug("Event payload: {Payload}", JsonSerializer.Serialize(eventPayload));
                return;
            }

            // Publish to AppSync Events via HTTP with IAM authentication
            await PublishEventAsync(eventApiHttpDomain, chatId, eventPayload, sessionEvent.EventType.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish session event: {EventType} for session: {SessionId}", sessionEvent.EventType, chatId);
            // Don't throw - event publishing should be non-blocking
        }
    }

    private async Task PublishEventAsync(string httpDomain, string chatId, object eventPayload, string eventType)
    {
        // Construct the channel path: /chat/{chatId}
        var channel = $"/chat/{chatId}";

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

            // Get the appropriate API key (Tenant or Consumer)
            var apiKey = _tenantEventApiKey; // Using TenantEventsApi for chat events
            if (!string.IsNullOrEmpty(apiKey))
            {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", apiKey);
                _logger.LogInformation("  API Key configured: {ApiKeyPrefix}...", apiKey.Length > 8 ? apiKey.Substring(0, 8) : "***");
            }
            else
            {
                _logger.LogWarning("API Key not configured for AppSync Events API");
                _logger.LogWarning("  _tenantEventApiKey is null or empty");
                _logger.LogWarning("  Configuration key checked: AWS:AppSync:TenantEventsApi:ApiKey");
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
        _logger.LogInformation("AppSync Events API Request Details:");
        _logger.LogInformation("  Auth Method: {AuthMethod}", UseApiKeyAuth ? "API Key" : "IAM/SigV4");
        _logger.LogInformation("  URL: {Url}", url);
        _logger.LogInformation("  Region: {Region}", _region);
        _logger.LogInformation("  Channel: {Channel}", channel);
        _logger.LogInformation("  Request Body: {RequestBody}", requestJson);

        // Log all headers for debugging
        _logger.LogInformation("  Request Headers:");
        foreach (var header in httpClient.DefaultRequestHeaders)
        {
            _logger.LogInformation("    {HeaderName}: {HeaderValue}", header.Key, string.Join(", ", header.Value));
        }
        foreach (var header in content.Headers)
        {
            _logger.LogInformation("    {HeaderName}: {HeaderValue}", header.Key, string.Join(", ", header.Value));
        }

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
            _logger.LogInformation("Successfully published event to AppSync Events API: {EventType} for chat: {ChatId}",
                eventType, chatId);
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to publish event to AppSync Events API. Status: {StatusCode}, Error: {Error}",
                response.StatusCode, errorContent);
        }
    }

    public async Task PublishMessageEventAsync(string chatId, ChatMessage message)
    {
        var sessionEvent = new ChatEvent
        {
            EventType = message.Role == ChatMessageRole.User ? ChatEventType.Message_received : ChatEventType.Message_completed,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow,
            Data = message
        };

        await PublishChatEventAsync(chatId, sessionEvent);
    }

    public async Task PublishChatStatusEventAsync(string chatId, ChatStatus status)
    {
        var sessionEvent = new ChatEvent
        {
            EventType = ChatEventType.Chat_status_changed,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow,
            Data = new { Status = status.ToString() }
        };

        await PublishChatEventAsync(chatId, sessionEvent);
    }

    public async Task PublishErrorEventAsync(string chatId, string error)
    {
        var sessionEvent = new ChatEvent
        {
            EventType = ChatEventType.Error_occurred,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow,
            Data = new { Error = error }
        };

        await PublishChatEventAsync(chatId, sessionEvent);
    }
}
