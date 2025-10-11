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
    Task AppendMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message);

    /// <summary>
    /// Saves all messages for a chat to DynamoDB, creating or replacing the entire record
    /// </summary>
    Task SaveAllMessagesAsync(ICallerInfo callerInfo, string chatId, List<ChatMessage> messages);

    /// <summary>
    /// Retrieves all messages for a chat from DynamoDB
    /// </summary>
    Task<List<ChatMessage>> GetMessagesAsync(ICallerInfo callerInfo, string chatId);
}
