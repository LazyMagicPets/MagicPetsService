using LazyMagic;

namespace ChatSchemaRepo;

/// <summary>
/// Interface for chat management service that handles in-memory chat state and background processing
/// </summary>
public interface IChatManagerService
{
    /// <summary>
    /// Initialize a new chat (sets up in-memory state, background tasks, generates IDs)
    /// </summary>
    Task<Chat> InitializeChatAsync(ICallerInfo callerInfo, Chat chat);

    /// <summary>
    /// Process a user message (queues for LLM processing, returns immediately)
    /// </summary>
    Task<ChatMessage> ProcessUserMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message);

    /// <summary>
    /// Get chat by ID from in-memory store
    /// </summary>
    Task<Chat> GetChatByIdAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Get chat message history (from in-memory store)
    /// </summary>
    Task<List<ChatMessage>> GetChatHistoryAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit);

    /// <summary>
    /// Update chat metadata and status
    /// </summary>
    Task<Chat> UpdateChatAsync(ICallerInfo callerInfo, Chat chat);

    /// <summary>
    /// Close chat and cleanup resources
    /// </summary>
    Task CloseChatAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Persist chat messages to DynamoDB without closing the chat
    /// </summary>
    Task PersistChatMessagesAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Get semaphore for keep-alive requests (for long-polling pattern)
    /// </summary>
    SemaphoreSlim? GetKeepAliveSemaphore(string chatId);
}
