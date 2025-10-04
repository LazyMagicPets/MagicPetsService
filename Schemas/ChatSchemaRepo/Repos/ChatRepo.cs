using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

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

        // Create empty ChatMessages record
        var chatMessages = new ChatMessages
        {
            Id = initializedChat.ChatMessagesId,
            ChatMessagesId = initializedChat.ChatMessagesId!,
            ChatId = initializedChat.ChatId!,
            Messages = new List<ChatMessage>(),
            CreateUtcTick = initializedChat.CreateUtcTick,
            UpdateUtcTick = initializedChat.UpdateUtcTick
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

        // Delete associated ChatMessages
        await _chatMessagesRepo.DeleteAsync(callerInfo, id);

        return new StatusCodeResult(200);
    }

    // Override ListAsync to return all chats (with pagination handled by caller)
    public override async Task<ObjectResult> ListAsync(ICallerInfo callerInfo, int limit = 0)
    {
        // Return all chats from DynamoDB (includes both active and inactive)
        return await base.ListAsync(callerInfo, limit);
    }
}
