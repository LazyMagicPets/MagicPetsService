
namespace ChatModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public interface IChatModuleController
    {

        /// <summary>
        /// Create new chat
        /// </summary>

        /// <remarks>
        /// Creates a new chat session
        /// </remarks>

        /// <returns>Chat created successfully</returns>

        Task<ActionResult<Chat>> ChatModuleAddChatAsync(Chat body);

        /// <summary>
        /// List chats
        /// </summary>

        /// <remarks>
        /// Lists all chats for the authenticated user
        /// </remarks>

        /// <returns>Chats retrieved successfully</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Chat>>> ChatModuleListChatsAsync();

        /// <summary>
        /// Update chat
        /// </summary>

        /// <remarks>
        /// Updates an existing chat
        /// </remarks>

        /// <returns>Chat updated successfully</returns>

        Task<ActionResult<Chat>> ChatModuleUpdateChatAsync(Chat body);

        /// <summary>
        /// Health check endpoint
        /// </summary>

        /// <remarks>
        /// Health check endpoint for service monitoring
        /// </remarks>

        /// <returns>Service is healthy</returns>

        Task<ActionResult<HealthCheckResponse>> ChatModuleHealthCheckAsync();

        /// <summary>
        /// Get chat by ID
        /// </summary>

        /// <remarks>
        /// Retrieves a chat by its ID
        /// </remarks>

        /// <param name="chatId">ID of the chat</param>

        /// <returns>Chat retrieved successfully</returns>

        Task<ActionResult<Chat>> ChatModuleGetChatByIdAsync(string chatId);

        /// <summary>
        /// Delete chat
        /// </summary>

        /// <remarks>
        /// Deletes a chat and cleans up resources
        /// </remarks>

        /// <param name="chatId">ID of the chat to delete</param>

        /// <returns>Chat deleted successfully</returns>

        Task<IActionResult> ChatModuleDeleteChatAsync(string chatId);

        /// <summary>
        /// Send message to chat
        /// </summary>

        /// <remarks>
        /// Sends a new message to an existing chat
        /// </remarks>

        /// <param name="chatId">ID of the chat</param>


        /// <returns>Message sent successfully</returns>

        Task<ActionResult<ChatMessage>> ChatModuleAddChatMessageAsync(string chatId, ChatMessage body);

        /// <summary>
        /// Get chat messages
        /// </summary>

        /// <remarks>
        /// Retrieves message history for a chat
        /// </remarks>

        /// <param name="chatId">ID of the chat</param>

        /// <param name="page">Page number for pagination</param>

        /// <param name="limit">Number of messages per page</param>

        /// <returns>Messages retrieved successfully</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<ChatMessage>>> ChatModuleGetChatMessagesAsync(string chatId, int? page = null, int? limit = null);

    }

}
