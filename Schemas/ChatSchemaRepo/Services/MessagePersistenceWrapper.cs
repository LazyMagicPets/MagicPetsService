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

    public async Task AppendMessageAsync(string chatId, ChatMessage message)
    {
        // Create a transient instance of ChatMessagesRepo to persist the message
        var chatMessagesRepo = _serviceProvider.GetRequiredService<IChatMessagesRepo>();
        await ((IMessagePersistence)chatMessagesRepo).AppendMessageAsync(chatId, message);
    }
}
