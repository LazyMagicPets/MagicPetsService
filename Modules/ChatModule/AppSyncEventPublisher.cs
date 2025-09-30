using Amazon.AppSync;
using System.Text.Json;

namespace ChatModule;

public class AppSyncEventPublisher
{
    private readonly IAmazonAppSync _appSyncClient;
    private readonly ILogger<AppSyncEventPublisher> _logger;

    public AppSyncEventPublisher(IAmazonAppSync appSyncClient, ILogger<AppSyncEventPublisher> logger)
    {
        _appSyncClient = appSyncClient;
        _logger = logger;
    }

    public async Task PublishSessionEventAsync(string sessionId, string eventType, object eventData)
    {
        try
        {
            var eventPayload = new
            {
                sessionId,
                eventType,
                data = eventData,
                timestamp = DateTime.UtcNow
            };

            var jsonPayload = JsonSerializer.Serialize(eventPayload);

            _logger.LogInformation("Publishing session event: {EventType} for session: {SessionId}", eventType, sessionId);

            // TODO: Implement actual AppSync Events API call once we have the endpoint configuration
            // For now, just log the event
            _logger.LogDebug("Event payload: {Payload}", jsonPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish session event: {EventType} for session: {SessionId}", eventType, sessionId);
            throw;
        }
    }

    public async Task PublishMessageEventAsync(string sessionId, string messageId, string content, string role)
    {
        await PublishSessionEventAsync(sessionId, "message", new
        {
            messageId,
            content,
            role,
            timestamp = DateTime.UtcNow
        });
    }

    public async Task PublishStatusEventAsync(string sessionId, string status)
    {
        await PublishSessionEventAsync(sessionId, "status", new
        {
            status,
            timestamp = DateTime.UtcNow
        });
    }
}