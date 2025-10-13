namespace ChatSchemaRepo;

/// <summary>
/// Singleton wrapper for IMessagePersistence that creates transient ChatContextsRepo instances.
/// This allows the singleton ChatManagerService to persist messages without violating DI lifetime rules.
/// </summary>
public class MessagePersistenceWrapper : IMessagePersistence
{
    private readonly IServiceProvider _serviceProvider;

    public MessagePersistenceWrapper(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task AppendMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message)
    {
        // Create a transient instance of ChatContextsRepo to persist the message
        var chatMessagesRepo = _serviceProvider.GetRequiredService<IChatContextRepo>();
        await ((IMessagePersistence)chatMessagesRepo).AppendMessageAsync(callerInfo, chatId, message);
    }

    public async Task SaveAllMessagesAsync(ICallerInfo callerInfo, string chatId, List<ChatMessage> messages)
    {
        // Create a transient instance of ChatContextsRepo to save all messages
        var chatMessagesRepo = _serviceProvider.GetRequiredService<IChatContextRepo>();
        await ((IMessagePersistence)chatMessagesRepo).SaveAllMessagesAsync(callerInfo, chatId, messages);
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(ICallerInfo callerInfo, string chatId)
    {
        // Create a transient instance of ChatContextsRepo to retrieve messages
        var chatMessagesRepo = _serviceProvider.GetRequiredService<IChatContextRepo>();
        var result = await ((IMessagePersistence)chatMessagesRepo).GetMessagesAsync(callerInfo, chatId);
        return result;
    }
}
