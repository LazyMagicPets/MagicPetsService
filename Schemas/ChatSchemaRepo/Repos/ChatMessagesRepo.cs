using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

// Extend the IChatMessagesRepo interface to add custom message operations
public partial interface IChatMessagesRepo : IDocumentRepo<ChatMessages>
{
    Task<ActionResult<ChatMessage>> AddMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message);
    Task<ActionResult<List<ChatMessage>>> GetMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit);
}

// Extend the ChatMessagesRepo class to implement custom message operations
public partial class ChatMessagesRepo : DYDBRepository<ChatMessages>, IChatMessagesRepo
{
    private readonly IChatManagerService _chatManagerService;

    // Constructor with additional IChatManagerService dependency
    public ChatMessagesRepo(IAmazonDynamoDB client, IChatManagerService chatManagerService) : base(client)
    {
        _chatManagerService = chatManagerService;
    }

    public async Task<ActionResult<ChatMessage>> AddMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message)
    {
        // Delegate to ChatManagerService for business logic (queuing, LLM processing)
        var userMessage = await _chatManagerService.ProcessUserMessageAsync(callerInfo, chatId, message);

        // Load ChatMessages, append message, save
        var messagesResult = await base.ReadAsync(callerInfo, chatId); // chatMessagesId == chatId for simplicity
        var chatMessages = messagesResult.Value;

        if (chatMessages == null)
        {
            return new NotFoundResult();
        }

        chatMessages.Messages ??= new List<ChatMessage>();
        chatMessages.Messages.Add(userMessage);
        chatMessages.UpdateUtcTick = DateTime.UtcNow.Ticks;

        await base.UpdateAsync(callerInfo, chatMessages);

        return new OkObjectResult(userMessage);
    }

    public async Task<ActionResult<List<ChatMessage>>> GetMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit)
    {
        // First try to get from ChatManagerService (for active in-memory chats)
        try
        {
            var messages = await _chatManagerService.GetChatHistoryAsync(callerInfo, chatId, page, limit);
            return new OkObjectResult(messages);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory, retrieve from DynamoDB
            var messagesResult = await base.ReadAsync(callerInfo, chatId);
            var chatMessages = messagesResult.Value;

            if (chatMessages == null)
            {
                return new NotFoundResult();
            }

            var pageNumber = page ?? 1;
            var pageSize = Math.Min(limit ?? 50, 100);
            var skip = (pageNumber - 1) * pageSize;

            var messages = chatMessages.Messages ?? new List<ChatMessage>();
            var paginatedMessages = messages
                .OrderBy(m => m.Timestamp)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new OkObjectResult(paginatedMessages);
        }
    }
}
