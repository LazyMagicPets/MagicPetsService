using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

/// <summary>
/// Interface for ChatManagerService - manages chat lifecycle, in-memory state, and background processing.
/// </summary>
public interface IChatManagerService
{
    /// <summary>
    /// Creates a new chat and starts background processing.
    /// </summary>
    Task<CreateChatResponse> CreateChatAsync(ICallerInfo callerInfo, CreateChatRequest body);

    /// <summary>
    /// Sends a message to an existing chat.
    /// </summary>
    Task<SendMessageResponse> SendMessageAsync(ICallerInfo callerInfo, string chatId, SendMessageRequest body);

    /// <summary>
    /// Retrieves the current status of a chat.
    /// </summary>
    Task<GetChatStatusResponse> GetChatStatusAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Retrieves a chat by its ID.
    /// </summary>
    Task<GetChatResponse> GetChatAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Updates chat metadata and status.
    /// </summary>
    Task<UpdateChatResponse> UpdateChatAsync(ICallerInfo callerInfo, string chatId, UpdateChatRequest body);

    /// <summary>
    /// Lists chats for the authenticated user with pagination.
    /// </summary>
    Task<ListChatsResponse> ListChatsAsync(ICallerInfo callerInfo, int? page, int? limit, ChatStatus? status);

    /// <summary>
    /// Retrieves paginated message history for a chat.
    /// </summary>
    Task<ChatMessagesResponse> GetChatMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit);

    /// <summary>
    /// Closes a chat and releases resources.
    /// </summary>
    Task<IActionResult> CloseChatAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Gets the keep-alive semaphore for a chat (used by internal keep-alive endpoint).
    /// </summary>
    SemaphoreSlim? GetKeepAliveSemaphore(string chatId);
}
