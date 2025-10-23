namespace ChatSchemaRepo;

/// <summary>
/// Domain-level chat event publisher interface.
/// Provides high-level methods for publishing chat-related events.
/// </summary>
public interface IChatEventPublisher
{
    /// <summary>
    /// Publishes user message received event
    /// </summary>
    Task PublishUserMessageAsync(string chatId, ChatMessage message, ICallerInfo callerInfo);

    /// <summary>
    /// Publishes assistant processing started event
    /// </summary>
    Task PublishProcessingStartedAsync(string chatId, string messageId, ICallerInfo callerInfo);

    /// <summary>
    /// Publishes streaming chunk event
    /// </summary>
    Task PublishStreamingChunkAsync(string chatId, string messageId, string chunk, ICallerInfo callerInfo);

    /// <summary>
    /// Publishes assistant message completed event
    /// </summary>
    Task PublishMessageCompletedAsync(string chatId, ChatMessage message, ICallerInfo callerInfo);

    /// <summary>
    /// Publishes error event
    /// </summary>
    Task PublishErrorAsync(string chatId, string error, ICallerInfo callerInfo);

    /// <summary>
    /// Publishes chat status changed event
    /// </summary>
    Task PublishStatusChangedAsync(string chatId, ChatStatus status, ICallerInfo callerInfo);
}
