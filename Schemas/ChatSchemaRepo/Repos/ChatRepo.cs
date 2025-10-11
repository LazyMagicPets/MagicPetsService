using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

// Extend the IChatRepo interface to add custom message operations
public partial interface IChatRepo : IDocumentRepo<Chat>
{
    Task<ActionResult<ChatMessage>> CreateMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message);
    Task<ActionResult<ICollection<ChatMessage>>> ReadMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit);
}

// Extend the ChatRepo class to override base CRUD methods with chat-specific logic
public partial class ChatRepo : DYDBRepository<Chat>, IChatRepo
{
    private readonly IChatManagerService _chatManagerService;
    private readonly IChatMessagesRepo _chatMessagesRepo;

    // Constructor with additional dependencies
    public ChatRepo(IAmazonDynamoDB client, IChatManagerService chatManagerService, IChatMessagesRepo chatMessagesRepo) : base(client)
    {
        _chatManagerService = chatManagerService;
        _chatMessagesRepo = chatMessagesRepo;
    }

    // Override CreateAsync to initialize chat with ChatManagerService
    public override async Task<ActionResult<Chat>> CreateAsync(ICallerInfo callerInfo, Chat chat)
    {
        // Initialize chat through ChatManagerService (sets up in-memory state, background processing)
        var initializedChat = await _chatManagerService.InitializeChatAsync(callerInfo, chat);

        // Persist to DynamoDB
        await base.CreateAsync(callerInfo, initializedChat);

        // Create empty ChatMessages record (using chatId as the primary key)
        // Let the base repository handle CreateUtcTick and UpdateUtcTick
        var chatMessages = new ChatMessages
        {
            Id = initializedChat.ChatId,
            ChatId = initializedChat.ChatId!,
            Messages = new List<ChatMessage>()
        };
        await _chatMessagesRepo.CreateAsync(callerInfo, chatMessages);

        return new OkObjectResult(initializedChat);
    }

    // Override ReadAsync to return from in-memory or DynamoDB
    public override async Task<ActionResult<Chat>> ReadAsync(ICallerInfo callerInfo, string id)
    {
        // First try to get from ChatManagerService (for active in-memory chats)
        try
        {
            var chat = await _chatManagerService.GetChatByIdAsync(callerInfo, id);
            return new OkObjectResult(chat);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory, retrieve from DynamoDB
            return await base.ReadAsync(callerInfo, id);
        }
    }

    // Override UpdateAsync to update both in-memory and DynamoDB
    public override async Task<ActionResult<Chat>> UpdateAsync(ICallerInfo callerInfo, Chat chat, bool forceUpdate = false)
    {
        // Update in ChatManagerService if active
        try
        {
            var updatedChat = await _chatManagerService.UpdateChatAsync(callerInfo, chat);

            // Persist to DynamoDB
            await base.UpdateAsync(callerInfo, updatedChat, forceUpdate);

            return new OkObjectResult(updatedChat);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory, just update DynamoDB
            return await base.UpdateAsync(callerInfo, chat, forceUpdate);
        }
    }

    // Override DeleteAsync to close chat and cleanup resources
    public override async Task<StatusCodeResult> DeleteAsync(ICallerInfo callerInfo, string id)
    {
        // First, retrieve the chat to get ChatMessagesId
        var chatResult = await base.ReadAsync(callerInfo, id);
        var chat = chatResult.Value;

        // Close chat in ChatManagerService (cleanup in-memory resources)
        try
        {
            await _chatManagerService.CloseChatAsync(callerInfo, id);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory, that's fine
        }

        // Delete from DynamoDB
        await base.DeleteAsync(callerInfo, id);

        // Delete associated ChatMessages using chatId
        if (chat?.ChatId != null)
        {
            await _chatMessagesRepo.DeleteAsync(callerInfo, chat.ChatId);
        }

        return new StatusCodeResult(200);
    }

    // Override ListAsync to return all chats (with pagination handled by caller)
    public override async Task<ObjectResult> ListAsync(ICallerInfo callerInfo, int limit = 0)
    {
        // Return all chats from DynamoDB (includes both active and inactive)
        return await base.ListAsync(callerInfo, limit);
    }

    /// <summary>
    /// Creates a new message in a chat - queues with ChatManagerService for background processing
    /// Messages are kept in memory and persisted when the chat is closed
    /// </summary>
    public async Task<ActionResult<ChatMessage>> CreateMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message)
    {
        try
        {
            // Queue message with ChatManagerService for background processing
            // The message is added to the in-memory queue and will be persisted when chat closes
            var userMessage = await _chatManagerService.ProcessUserMessageAsync(callerInfo, chatId, message);
            return new OkObjectResult(userMessage);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory - need to re-initialize from DynamoDB
            var chatResult = await base.ReadAsync(callerInfo, chatId);
            if (chatResult.Value == null)
            {
                return new NotFoundObjectResult($"Chat {chatId} not found");
            }

            var chat = chatResult.Value;

            // Re-initialize chat in memory
            var reinitializedChat = await _chatManagerService.InitializeChatAsync(callerInfo, chat);

            // Now process the message
            var userMessage = await _chatManagerService.ProcessUserMessageAsync(callerInfo, chatId, message);
            return new OkObjectResult(userMessage);
        }
    }

    /// <summary>
    /// Reads messages from a chat - first checks in-memory, then falls back to DynamoDB
    /// </summary>
    public async Task<ActionResult<ICollection<ChatMessage>>> ReadMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit)
    {
        // First try to get from ChatManagerService (for active in-memory chats)
        try
        {
            var messages = await _chatManagerService.GetChatHistoryAsync(callerInfo, chatId, page, limit);
            return new OkObjectResult(messages as ICollection<ChatMessage>);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory, retrieve from DynamoDB
            return await _chatMessagesRepo.ReadMessagesAsync(callerInfo, chatId, page, limit);
        }
    }
}
