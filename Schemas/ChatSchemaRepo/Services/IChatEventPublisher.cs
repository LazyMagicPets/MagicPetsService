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
    Task PublishUserMessageAsync(string chatId, ChatMessage message);

    /// <summary>
    /// Publishes assistant processing started event
    /// </summary>
    Task PublishProcessingStartedAsync(string chatId, string messageId);

    /// <summary>
    /// Publishes streaming chunk event
    /// </summary>
    Task PublishStreamingChunkAsync(string chatId, string messageId, string chunk);

    /// <summary>
    /// Publishes assistant message completed event
    /// </summary>
    Task PublishMessageCompletedAsync(string chatId, ChatMessage message);

    /// <summary>
    /// Publishes error event
    /// </summary>
    Task PublishErrorAsync(string chatId, string error);

    /// <summary>
    /// Publishes chat status changed event
    /// </summary>
    Task PublishStatusChangedAsync(string chatId, ChatStatus status);
}
