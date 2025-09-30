
namespace ChatModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public interface IChatModuleController
    {

        /// <summary>
        /// Create new chat session
        /// </summary>

        /// <remarks>
        /// Creates a new chat session and starts processing the initial message
        /// </remarks>

        /// <returns>Session created successfully</returns>

        Task<ActionResult<CreateSessionResponse>> ChatModuleCreateSessionAsync(CreateSessionRequest body);

        /// <summary>
        /// Health check endpoint
        /// </summary>

        /// <remarks>
        /// Health check endpoint for App Runner service monitoring
        /// </remarks>

        /// <returns>Service is healthy</returns>

        Task<ActionResult<HealthCheckResponse>> ChatModuleHealthCheckAsync();

        /// <summary>
        /// Send message to existing session
        /// </summary>

        /// <remarks>
        /// Sends a message to an existing chat session
        /// </remarks>

        /// <param name="sessionId">ID of the chat session</param>


        /// <returns>Message sent successfully</returns>

        Task<ActionResult<SendMessageResponse>> ChatModuleSendMessageAsync(string sessionId, SendMessageRequest body);

        /// <summary>
        /// Get session status
        /// </summary>

        /// <remarks>
        /// Retrieves the current status and information about a chat session
        /// </remarks>

        /// <param name="sessionId">ID of the chat session</param>

        /// <returns>Session status retrieved successfully</returns>

        Task<ActionResult<GetSessionStatusResponse>> ChatModuleGetSessionStatusAsync(string sessionId);

        /// <summary>
        /// Close chat session
        /// </summary>

        /// <remarks>
        /// Closes an active chat session and cleans up resources
        /// </remarks>

        /// <param name="sessionId">ID of the chat session to close</param>

        /// <returns>Session closed successfully</returns>

        Task<IActionResult> ChatModuleCloseSessionAsync(string sessionId);

        /// <summary>
        /// Get session message history
        /// </summary>

        /// <remarks>
        /// Retrieves the message history for a chat session
        /// </remarks>

        /// <param name="sessionId">ID of the chat session</param>

        /// <param name="page">Page number for pagination</param>

        /// <param name="limit">Number of messages per page</param>

        /// <returns>Message history retrieved successfully</returns>

        Task<ActionResult<SessionMessagesResponse>> ChatModuleGetSessionMessagesAsync(string sessionId, int? page = null, int? limit = null);

    }

}
