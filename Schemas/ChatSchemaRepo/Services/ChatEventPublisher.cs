namespace ChatSchemaRepo;

/// <summary>
/// Domain-level chat event publisher implementation.
/// Translates high-level chat events to WebSocket transport events.
/// </summary>
public class ChatEventPublisher : IChatEventPublisher
{
    private readonly IWsEventPublisher _wsPublisher;
    private readonly ILogger<ChatEventPublisher> _logger;

    public ChatEventPublisher(
        IWsEventPublisher wsPublisher,
        ILogger<ChatEventPublisher> logger)
    {
        _wsPublisher = wsPublisher;
        _logger = logger;
    }

    public async Task PublishUserMessageAsync(string chatId, ChatMessage message, ICallerInfo callerInfo)
    {
        _logger.LogInformation("Publishing user message for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_received.ToString(),
            data: message,
            metadata: new Dictionary<string, object>
            {
                { "dataType", nameof(ChatMessage) }
            },
            callerInfo: callerInfo);
    }

    public async Task PublishProcessingStartedAsync(string chatId, string messageId, ICallerInfo callerInfo)
    {
        _logger.LogInformation("Publishing processing started for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_processing.ToString(),
            data: new { MessageId = messageId },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "Object" }
            },
            callerInfo: callerInfo);
    }

    public async Task PublishStreamingChunkAsync(string chatId, string messageId, string chunk, ICallerInfo callerInfo)
    {
        _logger.LogDebug("Publishing streaming chunk for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_streaming.ToString(),
            data: new { MessageId = messageId, Chunk = chunk },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "MessageChunk" }
            },
            callerInfo: callerInfo);
    }

    public async Task PublishMessageCompletedAsync(string chatId, ChatMessage message, ICallerInfo callerInfo)
    {
        _logger.LogInformation("Publishing message completed for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_completed.ToString(),
            data: message,
            metadata: new Dictionary<string, object>
            {
                { "dataType", nameof(ChatMessage) }
            },
            callerInfo: callerInfo);
    }

    public async Task PublishErrorAsync(string chatId, string error, ICallerInfo callerInfo)
    {
        _logger.LogWarning("Publishing error for chat {ChatId}: {Error}", chatId, error);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Error_occurred.ToString(),
            data: new { Error = error },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "Object" }
            },
            callerInfo: callerInfo);
    }

    public async Task PublishStatusChangedAsync(string chatId, ChatStatus status, ICallerInfo callerInfo)
    {
        _logger.LogInformation("Publishing status changed for chat {ChatId}: {Status}", chatId, status);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Chat_status_changed.ToString(),
            data: new { Status = status.ToString() },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "Object" }
            },
            callerInfo: callerInfo);
    }
}
