using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using LazyMagic;

namespace ChatSchemaRepo;

/// <summary>
/// Manager for handling chats with in-memory state and background processing.
/// Manages chat lifecycle, message queuing, and background LLM processing.
/// </summary>
public class ChatManagerService : IChatManagerService, IHostedService
{
    private readonly ILogger<ChatManagerService> _logger;
    private readonly ILlmClient _llmClient;
    private readonly IChatEventPublisher _eventPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMessagePersistence _messagePersistence;
    private readonly IChatRepo _chatRepo;
    private readonly IChatContextRepo _contextRepo;
    private readonly ConcurrentDictionary<string, ConnectionChat> _chats;
    private readonly ConcurrentDictionary<string, Task> _backgroundTasks;
    private readonly SemaphoreSlim _keepAliveSemaphore;
    private Task? _keepAliveTask;
    private readonly Timer _cleanupTimer;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public ChatManagerService(
        ILogger<ChatManagerService> logger,
        ILlmClient llmClient,
        IChatEventPublisher eventPublisher,
        IHttpClientFactory httpClientFactory,
        IMessagePersistence messagePersistence,
        IChatRepo chatRepo,
        IChatContextRepo contextRepo)
    {
        _logger = logger;
        _llmClient = llmClient;
        _eventPublisher = eventPublisher;
        _httpClientFactory = httpClientFactory;
        _messagePersistence = messagePersistence;
        _chatRepo = chatRepo;
        _contextRepo = contextRepo;
        _chats = new ConcurrentDictionary<string, ConnectionChat>();
        _backgroundTasks = new ConcurrentDictionary<string, Task>();
        _keepAliveSemaphore = new SemaphoreSlim(0, 1);
        _cancellationTokenSource = new CancellationTokenSource();

        // Run cleanup every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredChats, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    #region Orchestrator Methods (API Entry Points)

    /// <summary>
    /// Creates a new chat and initializes it in memory for LLM processing.
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<Chat>> CreateChatAsync(ICallerInfo callerInfo, Chat chat)
    {
        _logger.LogInformation("DEBUG: CreateChatAsync called with input chatId: {InputChatId}", chat.ChatId);

        // 1. Get user ID from callerInfo
        var userId = callerInfo?.LzUserId ?? "unknown";

        // 2. Set initial values
        var newChatId = Guid.NewGuid().ToString();
        _logger.LogInformation("DEBUG: Generated new chatId: {NewChatId}", newChatId);
        chat.ChatId = newChatId;
        chat.Id = newChatId;
        chat.UserId = userId;  // Set from CallerInfo
        chat.Status = ChatStatus.Active;
        chat.CreatedAt = DateTimeOffset.UtcNow;
        chat.LastActivityAt = DateTimeOffset.UtcNow;
        chat.MessageCount = 0;

        // 3. Persist Chat to DynamoDB first (CreateUtcTick set by repo)
        _logger.LogInformation("DEBUG: About to persist Chat to DynamoDB");
        var chatResult = await _chatRepo.CreateAsync(callerInfo, chat);
        _logger.LogInformation("DEBUG: Chat persist - Value: {HasValue}, Result: {ResultType}",
            chatResult.Value != null, chatResult.Result?.GetType().Name ?? "null");

        // Extract chat from ActionResult<Chat> - check .Value first, then .Result
        Chat? persistedChat = chatResult.Value ?? (chatResult.Result as Microsoft.AspNetCore.Mvc.ObjectResult)?.Value as Chat;
        if (persistedChat == null)
        {
            _logger.LogError("DEBUG: Failed to persist Chat to DynamoDB - no value returned");
            return chatResult;
        }

        chat = persistedChat;
        _logger.LogInformation("DEBUG: Chat persisted successfully, chatId: {ChatId}", chat.ChatId);

        // 4. Create ChatContexts entity
        var chatContext = new ChatContext
        {
            Id = chat.ChatId,
            ChatId = chat.ChatId,
            Messages = new List<ChatMessage>()
        };

        _logger.LogInformation("DEBUG: About to persist ChatContexts to DynamoDB");
        var messagesResult = await _contextRepo.CreateAsync(callerInfo, chatContext);
        _logger.LogInformation("DEBUG: ChatContexts persist - Value: {HasValue}, Result: {ResultType}",
            messagesResult.Value != null, messagesResult.Result?.GetType().Name ?? "null");

        // Extract ChatContexts from ActionResult<ChatContexts> - check .Value first, then .Result
        ChatContext? persistedMessages = messagesResult.Value ?? (messagesResult.Result as Microsoft.AspNetCore.Mvc.ObjectResult)?.Value as ChatContext;
        if (persistedMessages == null)
        {
            _logger.LogError("DEBUG: Failed to persist ChatContexts, rolling back");
            // Rollback chat creation
            await _chatRepo.DeleteAsync(callerInfo, chat.ChatId);
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult("Failed to create chat messages");
        }
        _logger.LogInformation("DEBUG: ChatContexts persisted successfully");

        // 5. Initialize in-memory state
        var connectionChat = new ConnectionChat
        {
            ChatId = chat.ChatId,
            ChatContextsId = chat.ChatId,
            UserId = chat.UserId,
            Status = ChatStatus.Active,
            CreatedAt = chat.CreatedAt.DateTime,
            LastActivityAt = chat.LastActivityAt.DateTime,
            MessageQueue = Channel.CreateUnbounded<ChatMessage>(),
            CancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token),
            Context = chat.Metadata != null && chat.Metadata is IDictionary<string, object> metadataDict
                ? new Dictionary<string, object>(metadataDict)
                : new Dictionary<string, object>(),
            History = new List<ChatMessage>(),
            CallerInfo = callerInfo
        };

        if (!_chats.TryAdd(chat.ChatId, connectionChat))
        {
            _logger.LogWarning("Chat {ChatId} already exists in memory after creation", chat.ChatId);
        }

        // 6. Start background processing
        var backgroundTask = ProcessChatContextsAsync(connectionChat);
        _backgroundTasks.TryAdd(chat.ChatId, backgroundTask);

        // 7. Publish event
        await _eventPublisher.PublishStatusChangedAsync(chat.ChatId, ChatStatus.Active, callerInfo);

        _logger.LogInformation("DEBUG: About to log Created chat for {ChatId}", chat.ChatId);
        _logger.LogInformation("Created chat {ChatId} for user {UserId}", chat.ChatId, chat.UserId);
        _logger.LogInformation("DEBUG: Logged Created chat for {ChatId}", chat.ChatId);
        return new Microsoft.AspNetCore.Mvc.OkObjectResult(chat);
    }

    /// <summary>
    /// Gets a chat by ID. Returns in-memory instance if active, otherwise loads from DynamoDB.
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<Chat>> GetChatAsync(ICallerInfo callerInfo, string chatId)
    {
        // 1. Try in-memory first
        if (_chats.TryGetValue(chatId, out var connectionChat))
        {
            _logger.LogDebug("Retrieved chat {ChatId} from memory", chatId);

            var chat = new Chat
            {
                Id = chatId,
                ChatId = chatId,
                UserId = connectionChat.UserId,
                Status = connectionChat.Status,
                Summary = GenerateSummary(connectionChat.History),
                MessageCount = connectionChat.History.Count,
                CreatedAt = connectionChat.CreatedAt,
                LastActivityAt = connectionChat.LastActivityAt,
                Metadata = connectionChat.Context
            };

            return new Microsoft.AspNetCore.Mvc.OkObjectResult(chat);
        }

        // 2. Load from DynamoDB
        return await _chatRepo.ReadAsync(callerInfo, chatId);
    }

    /// <summary>
    /// Lists all chats for the current user.
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<ICollection<Chat>>> ListChatsAsync(ICallerInfo callerInfo)
    {
        // Get all chats from DynamoDB
        var result = await _chatRepo.ListAsync(callerInfo);

        if (result is not Microsoft.AspNetCore.Mvc.OkObjectResult ok)
            return result;

        var chats = ((ICollection<Chat>)ok.Value!).ToList();

        // Update with in-memory state if available
        for (int i = 0; i < chats.Count; i++)
        {
            var chat = chats[i];
            if (_chats.TryGetValue(chat.ChatId, out var connectionChat))
            {
                // Use in-memory version (may have updates not yet persisted)
                chats[i] = new Chat
                {
                    Id = connectionChat.ChatId,
                    ChatId = connectionChat.ChatId,
                    UserId = connectionChat.UserId,
                    Status = connectionChat.Status,
                    Summary = GenerateSummary(connectionChat.History),
                    MessageCount = connectionChat.History.Count,
                    CreatedAt = connectionChat.CreatedAt,
                    LastActivityAt = connectionChat.LastActivityAt,
                    Metadata = connectionChat.Context,
                    CreateUtcTick = chat.CreateUtcTick,
                    UpdateUtcTick = chat.UpdateUtcTick
                };
            }
        }

        return new Microsoft.AspNetCore.Mvc.OkObjectResult(chats);
    }

    /// <summary>
    /// Updates a chat. Updates both in-memory and persistent storage.
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<Chat>> UpdateChatAsync(
        ICallerInfo callerInfo,
        Chat chat)
    {
        // 1. Validate
        if (string.IsNullOrEmpty(chat.ChatId))
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult("ChatId is required");

        var chatId = chat.ChatId;

        // 2. Update in-memory first if exists
        if (_chats.TryGetValue(chatId, out var connectionChat))
        {
            // Update mutable properties
            connectionChat.Status = chat.Status;
            connectionChat.LastActivityAt = DateTime.UtcNow;

            if (chat.Metadata != null && chat.Metadata is IDictionary<string, object> metadata)
            {
                foreach (var kvp in metadata)
                {
                    connectionChat.Context[kvp.Key] = kvp.Value;
                }
            }

            // Update chat object from in-memory state
            chat.Status = connectionChat.Status;
            chat.Summary = GenerateSummary(connectionChat.History);
            chat.MessageCount = connectionChat.History.Count;
            chat.LastActivityAt = connectionChat.LastActivityAt;
            chat.Metadata = connectionChat.Context;
        }

        // 3. Persist to DynamoDB (UpdateUtcTick managed by repo)
        var result = await _chatRepo.UpdateAsync(callerInfo, chat);

        // Extract updated chat from ActionResult<Chat> - check .Value first, then .Result
        Chat? updatedChat = result.Value ?? (result.Result as Microsoft.AspNetCore.Mvc.ObjectResult)?.Value as Chat;
        if (updatedChat != null)
        {
            // 4. Publish event
            await _eventPublisher.PublishStatusChangedAsync(chatId, chat.Status, callerInfo);

            _logger.LogInformation("Updated chat {ChatId}", chatId);
        }

        return result;
    }

    /// <summary>
    /// Deletes a chat. Stops processing and removes from both memory and DynamoDB.
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.StatusCodeResult> DeleteChatAsync(ICallerInfo callerInfo, string chatId)
    {
        // 1. Stop in-memory processing if exists
        if (_chats.TryRemove(chatId, out var connectionChat))
        {
            connectionChat.CancellationToken.Cancel();
            connectionChat.MessageQueue.Writer.Complete();

            if (_backgroundTasks.TryRemove(chatId, out var task))
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error completing background task for chat {ChatId}", chatId);
                }
            }

            // Persist any remaining messages before deletion
            await PersistChatHistoryAsync(connectionChat.CallerInfo, chatId, connectionChat.History);

            connectionChat.CancellationToken.Dispose();

            _logger.LogInformation("Stopped in-memory processing for chat {ChatId}", chatId);
        }

        // 2. Delete ChatContexts
        await _contextRepo.DeleteAsync(callerInfo, chatId);

        // 3. Delete Chat
        var result = await _chatRepo.DeleteAsync(callerInfo, chatId);

        if (result.StatusCode == 200)
        {
            // 4. Publish event
            await _eventPublisher.PublishStatusChangedAsync(chatId, ChatStatus.Closed, callerInfo);

            _logger.LogInformation("Deleted chat {ChatId}", chatId);
        }

        return result;
    }

    /// <summary>
    /// Sends a message to a chat. Ensures chat is in memory and enqueues for LLM processing.
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<ChatMessage>> SendMessageAsync(
        ICallerInfo callerInfo,
        string chatId,
        ChatMessage message)
    {
        _logger.LogInformation("DEBUG: SendMessageAsync called for chat {ChatId}", chatId);

        // 1. Ensure chat exists and is in memory
        if (!_chats.TryGetValue(chatId, out var connectionChat))
        {
            _logger.LogWarning("DEBUG: Chat {ChatId} not found in memory, attempting resume", chatId);
            // Try to resume from DynamoDB
            var resumeResult = await ResumeChatAsync(callerInfo, chatId);
            if (resumeResult is not Microsoft.AspNetCore.Mvc.OkObjectResult)
            {
                _logger.LogError("DEBUG: Failed to resume chat {ChatId}", chatId);
                return new Microsoft.AspNetCore.Mvc.NotFoundObjectResult($"Chat {chatId} not found");
            }

            connectionChat = _chats[chatId];
        }

        _logger.LogInformation("DEBUG: Chat {ChatId} found in memory, status: {Status}", chatId, connectionChat.Status);

        // 2. Validate chat is active
        if (connectionChat.Status != ChatStatus.Active)
        {
            _logger.LogWarning("DEBUG: Chat {ChatId} is not active (status: {Status})", chatId, connectionChat.Status);
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult($"Chat {chatId} is not active");
        }

        // 3. Verify ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        _logger.LogInformation("DEBUG: Checking ownership - callerInfo userId: {UserId}, chat userId: {ChatUserId}", userId, connectionChat.UserId);
        if (connectionChat.UserId != userId)
        {
            _logger.LogWarning("DEBUG: User {UserId} does not own chat {ChatId} (owner: {OwnerId})", userId, chatId, connectionChat.UserId);
            return new Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult($"User {userId} does not own chat {chatId}");
        }

        // 4. Enrich message
        message.MessageId = Guid.NewGuid().ToString();
        message.ChatId = chatId;
        message.Timestamp = DateTime.UtcNow;
        message.Role = ChatMessageRole.User;

        // 5. Add to history (will be persisted by background processing)
        connectionChat.History.Add(message);

        // 6. Update chat metadata
        connectionChat.LastActivityAt = DateTime.UtcNow;

        // 7. Publish user message event
        await _eventPublisher.PublishUserMessageAsync(chatId, message, callerInfo);

        // 8. Enqueue for LLM processing
        await connectionChat.MessageQueue.Writer.WriteAsync(message, connectionChat.CancellationToken.Token);

        _logger.LogInformation("Sent message {MessageId} to chat {ChatId}", message.MessageId, chatId);
        return new Microsoft.AspNetCore.Mvc.OkObjectResult(message);
    }

    /// <summary>
    /// Gets messages for a chat. Returns in-memory messages if active, otherwise loads from DynamoDB.
    /// </summary>
    public async Task<Microsoft.AspNetCore.Mvc.ActionResult<ICollection<ChatMessage>>> GetMessagesAsync(
        ICallerInfo callerInfo,
        string chatId,
        int? page = null,
        int? limit = null)
    {
        // 1. Try in-memory first
        if (_chats.TryGetValue(chatId, out var connectionChat))
        {
            var messages = connectionChat.History.AsEnumerable();

            // Apply pagination if requested
            if (page.HasValue && limit.HasValue)
            {
                var skip = (page.Value - 1) * limit.Value;
                messages = messages
                    .Skip(skip)
                    .Take(limit.Value);
            }

            var messageList = messages.ToList();

            _logger.LogDebug("Retrieved {Count} messages from memory for chat {ChatId}",
                messageList.Count, chatId);

            return new Microsoft.AspNetCore.Mvc.OkObjectResult(messageList);
        }

        // 2. Load from DynamoDB via repo
        var result = await _contextRepo.ReadAsync(callerInfo, chatId);

        // Extract ChatContexts from ActionResult<ChatContexts> - check .Value first, then .Result
        ChatContext? chatMessages = result.Value ?? (result.Result as Microsoft.AspNetCore.Mvc.ObjectResult)?.Value as ChatContext;
        if (chatMessages == null)
            return new Microsoft.AspNetCore.Mvc.NotFoundObjectResult($"Chat {chatId} not found");
        var allMessages = chatMessages.Messages?.AsEnumerable() ?? Enumerable.Empty<ChatMessage>();

        // Apply pagination if requested
        if (page.HasValue && limit.HasValue)
        {
            var skip = (page.Value - 1) * limit.Value;
            allMessages = allMessages
                .Skip(skip)
                .Take(limit.Value);
        }

        var finalMessages = allMessages.ToList();

        _logger.LogDebug("Retrieved {Count} messages from DynamoDB for chat {ChatId}", finalMessages.Count, chatId);

        return new Microsoft.AspNetCore.Mvc.OkObjectResult(finalMessages);
    }

    /// <summary>
    /// Resumes a chat from DynamoDB into memory.
    /// </summary>
    private async Task<Microsoft.AspNetCore.Mvc.ActionResult> ResumeChatAsync(ICallerInfo callerInfo, string chatId)
    {
        // 1. Load Chat from DynamoDB
        var chatResult = await _chatRepo.ReadAsync(callerInfo, chatId);

        // Extract Chat from ActionResult<Chat> - check .Value first, then .Result
        Chat? chat = chatResult.Value ?? (chatResult.Result as Microsoft.AspNetCore.Mvc.ObjectResult)?.Value as Chat;
        if (chat == null)
            return new Microsoft.AspNetCore.Mvc.NotFoundObjectResult($"Chat {chatId} not found");

        // 2. Load ChatContexts from DynamoDB
        var messagesResult = await _contextRepo.ReadAsync(callerInfo, chatId);

        // Extract ChatContexts from ActionResult<ChatContexts> - check .Value first, then .Result
        ChatContext? chatMessages = messagesResult.Value ?? (messagesResult.Result as Microsoft.AspNetCore.Mvc.ObjectResult)?.Value as ChatContext;
        if (chatMessages == null)
        {
            // Create empty ChatContexts if not found
            chatMessages = new ChatContext
            {
                Id = chatId,
                ChatId = chatId,
                Messages = new List<ChatMessage>()
            };
        }

        // 3. Initialize in-memory state
        var connectionChat = new ConnectionChat
        {
            ChatId = chat.ChatId,
            ChatContextsId = chat.ChatId,
            UserId = chat.UserId,
            Status = chat.Status,
            CreatedAt = chat.CreatedAt.DateTime,
            LastActivityAt = chat.LastActivityAt.DateTime,
            MessageQueue = Channel.CreateUnbounded<ChatMessage>(),
            CancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token),
            Context = chat.Metadata != null && chat.Metadata is IDictionary<string, object> metadataDict
                ? new Dictionary<string, object>(metadataDict)
                : new Dictionary<string, object>(),
            History = chatMessages.Messages?.ToList() ?? new List<ChatMessage>(),
            CallerInfo = callerInfo
        };

        if (!_chats.TryAdd(chatId, connectionChat))
        {
            _logger.LogWarning("Chat {ChatId} already in memory during resume", chatId);
            return new Microsoft.AspNetCore.Mvc.OkObjectResult(chat);
        }

        // 4. Start background processing
        var backgroundTask = ProcessChatContextsAsync(connectionChat);
        _backgroundTasks.TryAdd(chatId, backgroundTask);

        // 5. Publish event
        await _eventPublisher.PublishStatusChangedAsync(chatId, chat.Status, callerInfo);

        _logger.LogInformation("Resumed chat {ChatId} from DynamoDB", chatId);
        return new Microsoft.AspNetCore.Mvc.OkObjectResult(chat);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Initiates a single long-polling HTTP request for the entire service.
    /// This prevents App Runner from scaling down the instance while any chats are active.
    /// The request waits on a semaphore that is released when the last chat is closed.
    /// Uses the Host header from CallerInfo to determine the service URL.
    /// </summary>
    private async Task InitiateKeepAliveAsync()
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("KeepAlive");

            // Get service host from any active chat's CallerInfo
            var serviceHost = GetServiceHost();

            _logger.LogDebug("Initiating keep-alive request for ChatManagerService at {ServiceHost}", serviceHost);

            // Long-polling request to our own internal endpoint
            var url = $"{serviceHost}/ChatModule/internal/keepalive";
            var response = await httpClient.PostAsync(url, null);

            _logger.LogDebug("Keep-alive request completed with status {StatusCode}", response.StatusCode);
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("Keep-alive request cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keep-alive request failed");
        }
        finally
        {
            _keepAliveTask = null;
        }
    }

    /// <summary>
    /// Gets the service host URL from active chats' CallerInfo headers.
    /// Falls back to localhost for local development.
    /// </summary>
    private string GetServiceHost()
    {
        // Try to get host from any active chat's CallerInfo
        var firstChat = _chats.Values.FirstOrDefault();
        if (firstChat?.CallerInfo?.Headers != null &&
            firstChat.CallerInfo.Headers.TryGetValue("Host", out var host) &&
            !string.IsNullOrEmpty(host))
        {
            // Determine scheme based on host
            var scheme = host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
            return $"{scheme}://{host}";
        }

        // Fallback to localhost for local development
        return "http://localhost:8080";
    }

    private async Task ProcessChatContextsAsync(ConnectionChat chat)
    {
        _logger.LogInformation("Started background processing for chat {ChatId}", chat.ChatId);

        try
        {
            await foreach (var message in chat.MessageQueue.Reader.ReadAllAsync(chat.CancellationToken.Token))
            {
                try
                {
                    // Update session status
                    chat.Status = ChatStatus.Processing;

                    // Process with LLM using streaming
                    var assistantMessageId = Guid.NewGuid().ToString();
                    var streamStartTime = DateTime.UtcNow;
                    var fullResponse = new System.Text.StringBuilder();

                    // Publish streaming start event
                    await _eventPublisher.PublishProcessingStartedAsync(chat.ChatId, assistantMessageId, chat.CallerInfo!);

                    // Stream the response
                    await foreach (var textChunk in _llmClient.GenerateResponseStreamAsync(chat.History, chat.CancellationToken.Token))
                    {
                        fullResponse.Append(textChunk);

                        // Publish streaming chunk event with only the new chunk
                        // Client can accumulate chunks on their end
                        await _eventPublisher.PublishStreamingChunkAsync(chat.ChatId, assistantMessageId, textChunk, chat.CallerInfo!);
                    }

                    // Create complete assistant message
                    var assistantMessage = new ChatMessage
                    {
                        MessageId = assistantMessageId,
                        ChatId = chat.ChatId,
                        Role = ChatMessageRole.Assistant,
                        Content = fullResponse.ToString(),
                        Timestamp = streamStartTime
                    };

                    // Add to session history
                    chat.History.Add(assistantMessage);
                    chat.LastActivityAt = DateTime.UtcNow;
                    chat.Status = ChatStatus.Active;

                    // Publish assistant response completed event
                    await _eventPublisher.PublishMessageCompletedAsync(chat.ChatId, assistantMessage, chat.CallerInfo!);

                    _logger.LogInformation("Processed streaming message for chat {ChatId}, total length: {Length}", chat.ChatId, fullResponse.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message for chat {ChatId}", chat.ChatId);

                    // Publish error event
                    await _eventPublisher.PublishErrorAsync(chat.ChatId, ex.Message, chat.CallerInfo!);

                    chat.Status = ChatStatus.Error;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Background processing cancelled for chat {ChatId}", chat.ChatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background processing failed for chat {ChatId}", chat.ChatId);
        }
    }

    private async Task CloseChatInternalAsync(string chatId)
    {
        if (_chats.TryRemove(chatId, out var chat))
        {
            chat.CancellationToken.Cancel();
            chat.MessageQueue.Writer.Complete();

            if (_backgroundTasks.TryRemove(chatId, out var task))
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelled
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error completing background task for chat {ChatId}", chatId);
                }
            }

            // Persist all messages to DynamoDB before closing
            await PersistChatHistoryAsync(chat.CallerInfo, chatId, chat.History);

            chat.CancellationToken.Dispose();
            _logger.LogInformation("Closed chat {ChatId}", chatId);

            // Keep-alive feature disabled for now
            // if (_chats.Count == 0)
            // {
            //     try
            //     {
            //         if (_keepAliveSemaphore.CurrentCount == 0)
            //         {
            //             _keepAliveSemaphore.Release();
            //             _logger.LogDebug("Released keep-alive semaphore (no active chats)");
            //         }
            //     }
            //     catch (Exception ex)
            //     {
            //         _logger.LogWarning(ex, "Error releasing keep-alive semaphore");
            //     }
            // }
        }
    }

    private void CleanupExpiredChats(object? state)
    {
        var expiredChats = _chats
            .Where(kvp => DateTime.UtcNow - kvp.Value.LastActivityAt > TimeSpan.FromMinutes(30))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var chatId in expiredChats)
        {
            _ = Task.Run(async () => await CloseChatInternalAsync(chatId));
        }

        if (expiredChats.Any())
        {
            _logger.LogInformation("Cleaned up {Count} expired sessions", expiredChats.Count);
        }
    }

    private string? GenerateSummary(List<ChatMessage> messages)
    {
        if (messages == null || messages.Count == 0)
            return null;

        // Get first user message as the summary (topic of conversation)
        var firstUserMessage = messages.FirstOrDefault(m => m.Role == ChatMessageRole.User);
        if (firstUserMessage == null)
            return null;

        // Truncate to 100 characters for summary
        var content = firstUserMessage.Content ?? string.Empty;
        return content.Length > 100 ? content.Substring(0, 97) + "..." : content;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ChatManagerService started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ChatManagerService stopping...");

        _cancellationTokenSource.Cancel();
        _cleanupTimer?.Dispose();

        // Close all chats
        var allChatIds = _chats.Keys.ToList();
        await Task.WhenAll(allChatIds.Select(CloseChatInternalAsync));

        // Cleanup keep-alive resources
        _keepAliveSemaphore.Dispose();

        _cancellationTokenSource.Dispose();
        _logger.LogInformation("ChatManagerService stopped");
    }

    /// <summary>
    /// Persists all messages in chat history to the ChatContexts table when chat closes.
    /// Creates or replaces the entire ChatContexts record with all messages at once.
    /// </summary>
    private async Task PersistChatHistoryAsync(ICallerInfo? callerInfo, string chatId, List<ChatMessage> history)
    {
        if (history == null || history.Count == 0)
        {
            _logger.LogDebug("No messages to persist for chat {ChatId}", chatId);
            return;
        }

        try
        {
            // Save all messages at once to avoid race conditions
            await _messagePersistence.SaveAllMessagesAsync(callerInfo!, chatId, history);

            _logger.LogInformation("Persisted {MessageCount} messages to DynamoDB for chat {ChatId}", history.Count, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist chat history to DynamoDB for chat {ChatId}", chatId);
            // Don't throw - we log the error but don't prevent chat closure
        }
    }

    #endregion
}

/// <summary>
/// Represents an active chat with in-memory state
/// </summary>
public class ConnectionChat
{
    public string ChatId { get; set; } = string.Empty;
    public string ChatContextsId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ChatStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public Channel<ChatMessage> MessageQueue { get; set; } = null!;
    public CancellationTokenSource CancellationToken { get; set; } = null!;
    public Dictionary<string, object> Context { get; set; } = new();
    public List<ChatMessage> History { get; set; } = new();
    public ICallerInfo? CallerInfo { get; set; } // Stores caller info for service host resolution
}
