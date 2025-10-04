using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

// Extend the IChatRepo interface to add custom API methods
public partial interface IChatRepo : IDocumentRepo<Chat>
{
    Task<CreateChatResponse> CreateChatAsync(ICallerInfo callerInfo, CreateChatRequest body);
    Task<SendMessageResponse> SendMessageAsync(ICallerInfo callerInfo, string chatId, SendMessageRequest body);
    Task<GetChatStatusResponse> GetChatStatusAsync(ICallerInfo callerInfo, string chatId);
    Task<GetChatResponse> GetChatAsync(ICallerInfo callerInfo, string chatId);
    Task<UpdateChatResponse> UpdateChatAsync(ICallerInfo callerInfo, string chatId, UpdateChatRequest body);
    Task<IActionResult> CloseChatAsync(ICallerInfo callerInfo, string chatId);
    Task<ListChatsResponse> ListChatsAsync(ICallerInfo callerInfo, int? page, int? limit, ChatStatus? status);
    Task<ChatMessagesResponse> GetChatMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit);
}

// Extend the ChatRepo class to implement custom API methods
public partial class ChatRepo : DYDBRepository<Chat>, IChatRepo
{
    private readonly IChatManagerService _chatManagerService;
    private readonly IChatMessagesRepo _chatMessagesRepo;

    // Constructor with additional dependencies
    // DI will select this constructor over the generated one
    public ChatRepo(IAmazonDynamoDB client, IChatManagerService chatManagerService, IChatMessagesRepo chatMessagesRepo) : base(client)
    {
        _chatManagerService = chatManagerService;
        _chatMessagesRepo = chatMessagesRepo;
    }

    // Custom API methods that delegate to ChatManagerService for business logic
    // and persist to DynamoDB
    public async Task<CreateChatResponse> CreateChatAsync(ICallerInfo callerInfo, CreateChatRequest body)
    {
        var response = await _chatManagerService.CreateChatAsync(callerInfo, body);

        // Persist the chat to DynamoDB using base repository method
        if (response.Chat != null)
        {
            await base.CreateAsync(callerInfo, response.Chat);

            // Create empty ChatMessages record
            var chatMessages = new ChatMessages
            {
                Id = response.Chat.ChatMessagesId,
                ChatMessagesId = response.Chat.ChatMessagesId,
                ChatId = response.Chat.ChatId,
                Messages = new List<ChatMessage>(),
                CreateUtcTick = response.Chat.CreateUtcTick,
                UpdateUtcTick = response.Chat.UpdateUtcTick
            };
            await _chatMessagesRepo.CreateAsync(callerInfo, chatMessages);
        }

        return response;
    }

    public async Task<SendMessageResponse> SendMessageAsync(ICallerInfo callerInfo, string chatId, SendMessageRequest body)
    {
        var response = await _chatManagerService.SendMessageAsync(callerInfo, chatId, body);

        // Update chat and persist messages
        if (response.Chat != null)
        {
            await base.UpdateAsync(callerInfo, response.Chat);

            // Load messages, append new message, save
            var messagesResult = await _chatMessagesRepo.ReadAsync(callerInfo, response.Chat.ChatMessagesId!);
            var chatMessages = messagesResult.Value;
            if (chatMessages != null)
            {
                chatMessages.Messages ??= new List<ChatMessage>();
                chatMessages.Messages.Add(response.Message);
                chatMessages.UpdateUtcTick = DateTime.UtcNow.Ticks;
                await _chatMessagesRepo.UpdateAsync(callerInfo, chatMessages);
            }
        }

        return response;
    }

    public async Task<GetChatStatusResponse> GetChatStatusAsync(ICallerInfo callerInfo, string chatId)
    {
        return await _chatManagerService.GetChatStatusAsync(callerInfo, chatId);
    }

    public async Task<GetChatResponse> GetChatAsync(ICallerInfo callerInfo, string chatId)
    {
        // First try to get from ChatManagerService (for active in-memory chats)
        try
        {
            return await _chatManagerService.GetChatAsync(callerInfo, chatId);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory, retrieve from DynamoDB
            var result = await base.ReadAsync(callerInfo, chatId);
            var chat = result.Value; // Unwrap ActionResult<Chat>
            return new GetChatResponse { Chat = chat };
        }
    }

    public async Task<UpdateChatResponse> UpdateChatAsync(ICallerInfo callerInfo, string chatId, UpdateChatRequest body)
    {
        var response = await _chatManagerService.UpdateChatAsync(callerInfo, chatId, body);

        // Persist the updated chat to DynamoDB
        if (response.Chat != null)
        {
            await base.UpdateAsync(callerInfo, response.Chat);
        }

        return response;
    }

    public async Task<IActionResult> CloseChatAsync(ICallerInfo callerInfo, string chatId)
    {
        var result = await _chatManagerService.CloseChatAsync(callerInfo, chatId);

        // Update the chat status in DynamoDB
        if (result is NoContentResult)
        {
            var chatResult = await base.ReadAsync(callerInfo, chatId);
            var chat = chatResult.Value; // Unwrap ActionResult<Chat>
            if (chat != null)
            {
                chat.Status = ChatStatus.Closed;
                chat.UpdateUtcTick = DateTime.UtcNow.Ticks;
                await base.UpdateAsync(callerInfo, chat);
            }
        }

        return result;
    }

    public async Task<ListChatsResponse> ListChatsAsync(ICallerInfo callerInfo, int? page, int? limit, ChatStatus? status)
    {
        // Read from DynamoDB for persistence across sessions
        var pageNumber = page ?? 1;
        var pageSize = Math.Min(limit ?? 50, 100);

        // Use base repository to list all chats
        var listResult = await base.ListAsync(callerInfo);
        var allChats = (listResult as ObjectResult)?.Value as List<Chat> ?? new List<Chat>();

        // Filter by status if provided
        var filteredChats = status.HasValue
            ? allChats.Where(c => c.Status == status.Value).ToList()
            : allChats;

        // Apply pagination
        var skip = (pageNumber - 1) * pageSize;
        var paginatedChats = filteredChats
            .OrderByDescending(c => c.LastActivityAt)
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        return new ListChatsResponse
        {
            Chats = paginatedChats,
            Pagination = new ChatPaginationInfo
            {
                Page = pageNumber,
                Limit = pageSize,
                TotalChats = filteredChats.Count,
                HasMore = filteredChats.Count > skip + pageSize
            }
        };
    }

    public async Task<ChatMessagesResponse> GetChatMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit)
    {
        // First try to get from ChatManagerService (for active in-memory chats)
        try
        {
            return await _chatManagerService.GetChatMessagesAsync(callerInfo, chatId, page, limit);
        }
        catch (InvalidOperationException)
        {
            // Chat not in memory, retrieve messages from DynamoDB
            var chatResult = await base.ReadAsync(callerInfo, chatId);
            var chat = chatResult.Value;

            if (chat?.ChatMessagesId == null)
                throw new InvalidOperationException($"Chat {chatId} has no messages");

            var messagesResult = await _chatMessagesRepo.ReadAsync(callerInfo, chat.ChatMessagesId);
            var chatMessages = messagesResult.Value;

            var pageNumber = page ?? 1;
            var pageSize = Math.Min(limit ?? 50, 100);
            var skip = (pageNumber - 1) * pageSize;

            var messages = chatMessages?.Messages ?? new List<ChatMessage>();
            var paginatedMessages = messages
                .OrderBy(m => m.Timestamp)
                .Skip(skip)
                .Take(pageSize)
                .ToList();

            return new ChatMessagesResponse
            {
                Messages = paginatedMessages,
                Pagination = new PaginationInfo
                {
                    Page = pageNumber,
                    Limit = pageSize,
                    TotalMessages = messages.Count,
                    HasMore = messages.Count > skip + pageSize
                }
            };
        }
    }
}
