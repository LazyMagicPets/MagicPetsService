using Amazon.AppSync;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace ChatSchemaRepo;

public class AppSyncEventPublisher : IAppSyncEventPublisher
{
    private readonly IAmazonAppSync _appSyncClient;
    private readonly ILogger<AppSyncEventPublisher> _logger;
    private readonly IConfiguration _configuration;
    private readonly string? _eventApiId;

    public AppSyncEventPublisher(
        IAmazonAppSync appSyncClient,
        ILogger<AppSyncEventPublisher> logger,
        IConfiguration configuration)
    {
        _appSyncClient = appSyncClient;
        _logger = logger;
        _configuration = configuration;
        _eventApiId = _configuration["AWS:AppSync:EventApiId"];
    }

    public async Task PublishChatEventAsync(string chatId, ChatEvent sessionEvent)
    {
        try
        {
            var eventPayload = new
            {
                chatId,
                eventType = sessionEvent.EventType.ToString(),
                data = sessionEvent.Data,
                timestamp = sessionEvent.Timestamp
            };

            var jsonPayload = JsonSerializer.Serialize(eventPayload);

            _logger.LogInformation("Publishing session event: {EventType} for session: {SessionId}", sessionEvent.EventType, chatId);

            if (string.IsNullOrEmpty(_eventApiId))
            {
                _logger.LogWarning("AppSync Event API ID not configured, logging event instead");
                _logger.LogDebug("Event payload: {Payload}", jsonPayload);
                return;
            }

            // TODO: Implement actual AppSync Events API call
            // For now, just log the event as the AppSync Events SDK might not be fully available
            _logger.LogInformation("Would publish to AppSync Events API {ApiId}: {Payload}", _eventApiId, jsonPayload);

            // Simulate async operation
            await Task.Delay(10);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish session event: {EventType} for session: {SessionId}", sessionEvent.EventType, chatId);
            // Don't throw - event publishing should be non-blocking
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
