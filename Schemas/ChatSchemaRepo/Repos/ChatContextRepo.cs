using LazyMagic;
using Microsoft.AspNetCore.Mvc;

namespace ChatSchemaRepo;

// Extend the IChatContextRepo interface to add custom message operations
public partial interface IChatContextRepo : IDocumentRepo<ChatContext>
{
    Task<ActionResult<ICollection<ChatMessage>>> ReadMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit);
}

// Extend the ChatContextRepo class to implement custom message operations
public partial class ChatContextRepo : DYDBRepository<ChatContext>, IChatContextRepo, IMessagePersistence
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
    /// Implementation of IMessagePersistence - appends a message to ChatContext in DynamoDB
    /// </summary>
    public async Task AppendMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message)
    {
        // Load ChatContext from DynamoDB
        var messagesResult = await base.ReadAsync(callerInfo, chatId);
        var chatMessages = messagesResult.Value;

        if (chatMessages == null)
        {
            // Create new ChatContext record if it doesn't exist
            // Let the base repository handle CreateUtcTick and UpdateUtcTick
            chatMessages = new ChatContext
            {
                Id = chatId,
                ChatId = chatId,
                Messages = new List<ChatMessage> { message }
            };

            // Create the record
            await base.CreateAsync(callerInfo, chatMessages);
        }
        else
        {
            // Append message to existing record
            // Let the base repository handle UpdateUtcTick for optimistic locking
            chatMessages.Messages ??= new List<ChatMessage>();
            chatMessages.Messages.Add(message);

            // Update the record
            await base.UpdateAsync(callerInfo, chatMessages);
        }
    }

    /// <summary>
    /// Implementation of IMessagePersistence - saves all messages for a chat, creating or replacing the record
    /// </summary>
    public async Task SaveAllMessagesAsync(ICallerInfo callerInfo, string chatId, List<ChatMessage> messages)
    {
        // Try to read existing record
        var messagesResult = await base.ReadAsync(callerInfo, chatId);
        var chatMessages = messagesResult.Value;

        if (chatMessages == null)
        {
            // Create new record with all messages
            // Let the base repository handle CreateUtcTick and UpdateUtcTick
            chatMessages = new ChatContext
            {
                Id = chatId,
                ChatId = chatId,
                Messages = messages
            };
            await base.CreateAsync(callerInfo, chatMessages);
        }
        else
        {
            // Replace all messages in existing record
            // Let the base repository handle UpdateUtcTick for optimistic locking
            chatMessages.Messages = messages;
            await base.UpdateAsync(callerInfo, chatMessages);
        }
    }

    /// <summary>
    /// Implementation of IMessagePersistence - retrieves all messages for a chat from DynamoDB
    /// </summary>
    public async Task<List<ChatMessage>> GetMessagesAsync(ICallerInfo callerInfo, string chatId)
    {
        // Load ChatContext from DynamoDB
        var messagesResult = await base.ReadAsync(callerInfo, chatId);
        var chatMessages = messagesResult.Value;

        if (chatMessages == null || chatMessages.Messages == null)
        {
            return new List<ChatMessage>();
        }

        return chatMessages.Messages.ToList();
    }
}
