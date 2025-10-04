
namespace ChatModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public interface IChatModuleController
    {

        /// <summary>
        /// Create new chat
        /// </summary>

        /// <remarks>
        /// Creates a new chat and starts processing the initial message
        /// </remarks>

        /// <returns>Chat created successfully</returns>

        Task<ActionResult<CreateChatResponse>> ChatModuleCreateChatAsync(CreateChatRequest body);

        /// <summary>
        /// List chats
        /// </summary>

        /// <remarks>
        /// Lists all chats for the authenticated user with pagination
        /// </remarks>

        /// <param name="page">Page number for pagination</param>

        /// <param name="limit">Number of chats per page</param>

        /// <param name="status">Filter by chat status</param>

        /// <returns>Chats retrieved successfully</returns>

        Task<ActionResult<ListChatsResponse>> ChatModuleListChatsAsync(int? page = null, int? limit = null, ChatStatus? status = null);

        /// <summary>
        /// Health check endpoint
        /// </summary>

        /// <remarks>
        /// Health check endpoint for App Runner service monitoring
        /// </remarks>

        /// <returns>Service is healthy</returns>

        Task<ActionResult<HealthCheckResponse>> ChatModuleHealthCheckAsync();

        /// <summary>
        /// Send message to existing chat
        /// </summary>

        /// <remarks>
        /// Sends a message to an existing chat
        /// </remarks>

        /// <param name="chatId">ID of the chat</param>


        /// <returns>Message sent successfully</returns>

        Task<ActionResult<SendMessageResponse>> ChatModuleSendMessageAsync(string chatId, SendMessageRequest body);

        /// <summary>
        /// Get chat status
        /// </summary>

        /// <remarks>
        /// Retrieves the current status and information about a chat
        /// </remarks>

        /// <param name="chatId">ID of the chat</param>

        /// <returns>Chat status retrieved successfully</returns>

        Task<ActionResult<GetChatStatusResponse>> ChatModuleGetChatStatusAsync(string chatId);

        /// <summary>
        /// Get chat by ID
        /// </summary>

        /// <remarks>
        /// Retrieves a chat by its ID from persistent storage
        /// </remarks>

        /// <param name="chatId">ID of the chat</param>

        /// <returns>Chat retrieved successfully</returns>

        Task<ActionResult<GetChatResponse>> ChatModuleGetChatAsync(string chatId);

        /// <summary>
        /// Update chat
        /// </summary>

        /// <remarks>
        /// Updates chat metadata and status in persistent storage
        /// </remarks>

        /// <param name="chatId">ID of the chat to update</param>


        /// <returns>Chat updated successfully</returns>

        Task<ActionResult<UpdateChatResponse>> ChatModuleUpdateChatAsync(string chatId, UpdateChatRequest body);

        /// <summary>
        /// Close chat
        /// </summary>

        /// <remarks>
        /// Closes an active chat and cleans up resources
        /// </remarks>

        /// <param name="chatId">ID of the chat to close</param>

        /// <returns>Chat closed successfully</returns>

        Task<IActionResult> ChatModuleCloseChatAsync(string chatId);

        /// <summary>
        /// Get chat message history
        /// </summary>

        /// <remarks>
        /// Retrieves the message history for a chat
        /// </remarks>

        /// <param name="chatId">ID of the chat</param>

        /// <param name="page">Page number for pagination</param>

        /// <param name="limit">Number of messages per page</param>

        /// <returns>Message history retrieved successfully</returns>

        Task<ActionResult<ChatMessagesResponse>> ChatModuleGetChatMessagesAsync(string chatId, int? page = null, int? limit = null);

    }

}
