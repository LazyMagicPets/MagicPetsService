using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

/// <summary>
/// Interface for chat management service that handles in-memory chat state and background processing
/// </summary>
public interface IChatManagerService
{
    #region Orchestrator Methods (API Entry Points)

    /// <summary>
    /// Creates a new chat and initializes it in memory for LLM processing
    /// </summary>
    Task<ActionResult<Chat>> CreateChatAsync(ICallerInfo callerInfo, Chat chat);

    /// <summary>
    /// Gets a chat by ID (from memory or DynamoDB)
    /// </summary>
    Task<ActionResult<Chat>> GetChatAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Lists all chats for the current user
    /// </summary>
    Task<ActionResult<ICollection<Chat>>> ListChatsAsync(ICallerInfo callerInfo);

    /// <summary>
    /// Updates a chat (both memory and DynamoDB)
    /// </summary>
    Task<ActionResult<Chat>> UpdateChatAsync(ICallerInfo callerInfo, Chat chat);

    /// <summary>
    /// Deletes a chat (from memory and DynamoDB)
    /// </summary>
    Task<StatusCodeResult> DeleteChatAsync(ICallerInfo callerInfo, string chatId);

    /// <summary>
    /// Sends a message to a chat (ensures in memory, enqueues for LLM)
    /// </summary>
    Task<ActionResult<ChatMessage>> SendMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message);

    /// <summary>
    /// Gets messages for a chat (from memory or DynamoDB)
    /// </summary>
    Task<ActionResult<ICollection<ChatMessage>>> GetMessagesAsync(ICallerInfo callerInfo, string chatId, int? page = null, int? limit = null);

    #endregion
}
