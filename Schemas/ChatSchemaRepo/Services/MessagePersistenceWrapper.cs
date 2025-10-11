namespace ChatSchemaRepo;

/// <summary>
/// Singleton wrapper for IMessagePersistence that creates transient ChatMessagesRepo instances.
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
        // Create a transient instance of ChatMessagesRepo to persist the message
        var chatMessagesRepo = _serviceProvider.GetRequiredService<IChatMessagesRepo>();
        await ((IMessagePersistence)chatMessagesRepo).AppendMessageAsync(callerInfo, chatId, message);
    }

    public async Task SaveAllMessagesAsync(ICallerInfo callerInfo, string chatId, List<ChatMessage> messages)
    {
        // Create a transient instance of ChatMessagesRepo to save all messages
        var chatMessagesRepo = _serviceProvider.GetRequiredService<IChatMessagesRepo>();
        await ((IMessagePersistence)chatMessagesRepo).SaveAllMessagesAsync(callerInfo, chatId, messages);
    }

    public async Task<List<ChatMessage>> GetMessagesAsync(ICallerInfo callerInfo, string chatId)
    {
        // Create a transient instance of ChatMessagesRepo to retrieve messages
        var chatMessagesRepo = _serviceProvider.GetRequiredService<IChatMessagesRepo>();
        var result = await ((IMessagePersistence)chatMessagesRepo).GetMessagesAsync(callerInfo, chatId);
        return result;
    }
}
