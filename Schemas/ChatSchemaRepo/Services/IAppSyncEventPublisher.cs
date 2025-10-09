namespace ChatSchemaRepo;

/// <summary>
/// Interface for publishing chat events to clients
/// </summary>
public interface IAppSyncEventPublisher
{
    /// <summary>
    /// Publishes a chat event
    /// </summary>
    Task PublishChatEventAsync(string chatId, ChatEvent sessionEvent);

    /// <summary>
    /// Publishes a message event (user message received or assistant message completed)
    /// </summary>
    Task PublishMessageEventAsync(string chatId, ChatMessage message);

    /// <summary>
    /// Publishes a chat status change event
    /// </summary>
    Task PublishChatStatusEventAsync(string chatId, ChatStatus status);

    /// <summary>
    /// Publishes an error event
    /// </summary>
    Task PublishErrorEventAsync(string chatId, string error);
}
