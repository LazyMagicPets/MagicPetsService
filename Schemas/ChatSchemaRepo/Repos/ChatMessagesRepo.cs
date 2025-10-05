using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

// Extend the IChatMessagesRepo interface to add custom message operations
public partial interface IChatMessagesRepo : IDocumentRepo<ChatMessages>
{
    Task<ActionResult<ICollection<ChatMessage>>> ReadMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit);
}

// Extend the ChatMessagesRepo class to implement custom message operations
public partial class ChatMessagesRepo : DYDBRepository<ChatMessages>, IChatMessagesRepo, IMessagePersistence
{
    public async Task<ActionResult<ICollection<ChatMessage>>> ReadMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit)
    {
        // Retrieve from DynamoDB
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

        return new OkObjectResult(paginatedMessages as ICollection<ChatMessage>);
    }

    /// <summary>
    /// Implementation of IMessagePersistence - appends a message to ChatMessages in DynamoDB
    /// </summary>
    public async Task AppendMessageAsync(string chatId, ChatMessage message)
    {
        // Load ChatMessages from DynamoDB (no CallerInfo needed for internal operation)
        var messagesResult = await base.ReadAsync(null!, chatId);
        var chatMessages = messagesResult.Value;

        if (chatMessages == null)
        {
            throw new InvalidOperationException($"ChatMessages record not found for chat {chatId}");
        }

        // Append message
        chatMessages.Messages ??= new List<ChatMessage>();
        chatMessages.Messages.Add(message);
        chatMessages.UpdateUtcTick = DateTime.UtcNow.Ticks;

        // Persist to DynamoDB (no CallerInfo needed for internal operation)
        await base.UpdateAsync(null!, chatMessages);
    }
}
