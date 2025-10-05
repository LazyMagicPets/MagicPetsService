namespace ChatSchemaRepo;

/// <summary>
/// Simple interface for persisting chat messages to storage.
/// Used by ChatManagerService to avoid circular dependency with IChatMessagesRepo.
/// </summary>
public interface IMessagePersistence
{
    /// <summary>
    /// Appends a message to the ChatMessages record in DynamoDB
    /// </summary>
    Task AppendMessageAsync(string chatId, ChatMessage message);
}
