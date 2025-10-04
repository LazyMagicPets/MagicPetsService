using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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
    private readonly AppSyncEventPublisher _eventPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, ConnectionChat> _chats;
    private readonly ConcurrentDictionary<string, Task> _backgroundTasks;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keepAliveSemaphores;
    private readonly Timer _cleanupTimer;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public ChatManagerService(
        ILogger<ChatManagerService> logger,
        ILlmClient llmClient,
        AppSyncEventPublisher eventPublisher,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _llmClient = llmClient;
        _eventPublisher = eventPublisher;
        _httpClientFactory = httpClientFactory;
        _chats = new ConcurrentDictionary<string, ConnectionChat>();
        _backgroundTasks = new ConcurrentDictionary<string, Task>();
        _keepAliveSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
        _cancellationTokenSource = new CancellationTokenSource();

        // Run cleanup every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredChats, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task<CreateChatResponse> CreateChatAsync(ICallerInfo callerInfo, CreateChatRequest body)
    {
        var chatId = Guid.NewGuid().ToString();
        var chatMessagesId = Guid.NewGuid().ToString();
        var userId = callerInfo?.LzUserId ?? "unknown";

        var chat = new ConnectionChat
        {
            ChatId = chatId,
            ChatMessagesId = chatMessagesId,
            UserId = userId,
            Status = ChatStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            MessageQueue = Channel.CreateUnbounded<ChatMessage>(),
            CancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token),
            Context = new Dictionary<string, object>(),
            History = new List<ChatMessage>()
        };

        _chats.TryAdd(chatId, chat);

        // Create semaphore for keep-alive request (starts unreleased)
        var semaphore = new SemaphoreSlim(0, 1);
        _keepAliveSemaphores.TryAdd(chatId, semaphore);

        // Start background processing task for this session
        var backgroundTask = ProcessChatMessagesAsync(chat);
        _backgroundTasks.TryAdd(chatId, backgroundTask);

        // Initiate keep-alive long-polling request to represent this chat as active load
        _ = InitiateKeepAliveAsync(chatId);

        _logger.LogInformation("Created session {ChatId} for user {UserId}", chatId, userId);

        var now = DateTime.UtcNow;
        var nowTicks = now.Ticks;

        return new CreateChatResponse
        {
            Chat = new Chat
            {
                Id = chatId,
                ChatId = chatId,
                ChatMessagesId = chatMessagesId,
                UserId = userId,
                Status = ChatStatus.Active,
                Summary = null, // Will be updated after first exchange
                MessageCount = 0,
                CreatedAt = chat.CreatedAt,
                LastActivityAt = chat.LastActivityAt,
                CreateUtcTick = nowTicks,
                UpdateUtcTick = nowTicks
            }
        };
    }

    public async Task<SendMessageResponse> SendMessageAsync(ICallerInfo callerInfo, string chatId, SendMessageRequest body)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify session ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        // Create user message
        var messageId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var userMessage = new ChatMessage
        {
            MessageId = messageId,
            ChatId = chatId,
            Role = ChatMessageRole.User,
            Content = body.Content,
            Timestamp = now
        };

        // Add to session history
        chat.History.Add(userMessage);
        chat.LastActivityAt = DateTime.UtcNow;
        chat.Status = ChatStatus.Processing;

        // Queue message for background processing
        await chat.MessageQueue.Writer.WriteAsync(userMessage, chat.CancellationToken.Token);

        _logger.LogInformation("Queued message for chat {ChatId}", chatId);

        return new SendMessageResponse
        {
            Message = userMessage,
            Chat = new Chat
            {
                Id = chatId,
                ChatId = chatId,
                ChatMessagesId = chat.ChatMessagesId,
                UserId = userId,
                Status = ChatStatus.Processing,
                Summary = GenerateSummary(chat.History),
                MessageCount = chat.History.Count,
                CreatedAt = chat.CreatedAt,
                LastActivityAt = chat.LastActivityAt,
                CreateUtcTick = chat.CreatedAt.Ticks,
                UpdateUtcTick = DateTime.UtcNow.Ticks
            }
        };
    }

    public async Task<GetChatStatusResponse> GetChatStatusAsync(ICallerInfo callerInfo, string chatId)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify session ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        await Task.Delay(0); // Keep async

        return new GetChatStatusResponse
        {
            Chat = new Chat
            {
                Id = chatId,
                ChatId = chatId,
                ChatMessagesId = chat.ChatMessagesId,
                UserId = userId,
                Status = chat.Status,
                Summary = GenerateSummary(chat.History),
                MessageCount = chat.History.Count,
                CreatedAt = chat.CreatedAt,
                LastActivityAt = chat.LastActivityAt,
                CreateUtcTick = chat.CreatedAt.Ticks,
                UpdateUtcTick = chat.LastActivityAt.Ticks
            },
            MessageCount = chat.History.Count
        };
    }

    public async Task<IActionResult> CloseChatAsync(ICallerInfo callerInfo, string chatId)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            return new NotFoundResult();
        }

        // Verify session ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            return new ForbidResult();
        }

        await CloseChatInternalAsync(chatId);
        return new NoContentResult();
    }

    public async Task<ChatMessagesResponse> GetChatMessagesAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify session ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        var pageNumber = page ?? 1;
        var pageSize = Math.Min(limit ?? 50, 100); // Cap at 100 messages per page
        var skip = (pageNumber - 1) * pageSize;

        var messages = chat.History
            .OrderBy(m => m.Timestamp)
            .Skip(skip)
            .Take(pageSize)
            .ToList();

        await Task.Delay(0); // Keep async

        return new ChatMessagesResponse
        {
            Messages = messages,
            Pagination = new PaginationInfo
            {
                Page = pageNumber,
                Limit = pageSize,
                TotalMessages = chat.History.Count,
                HasMore = chat.History.Count > skip + pageSize
            }
        };
    }

    public async Task<GetChatResponse> GetChatAsync(ICallerInfo callerInfo, string chatId)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify session ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        await Task.Delay(0); // Keep async

        return new GetChatResponse
        {
            Chat = new Chat
            {
                Id = chatId,
                ChatId = chatId,
                ChatMessagesId = chat.ChatMessagesId,
                UserId = userId,
                Status = chat.Status,
                Summary = GenerateSummary(chat.History),
                MessageCount = chat.History.Count,
                CreatedAt = chat.CreatedAt,
                LastActivityAt = chat.LastActivityAt,
                Metadata = chat.Context.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                CreateUtcTick = chat.CreatedAt.Ticks,
                UpdateUtcTick = chat.LastActivityAt.Ticks
            }
        };
    }

    public async Task<UpdateChatResponse> UpdateChatAsync(ICallerInfo callerInfo, string chatId, UpdateChatRequest body)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify session ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        // Update status (note: Status is non-nullable enum in DTO, defaults to Active if not sent in JSON)
        // TODO: Consider making UpdateChatRequest.Status nullable in schema to distinguish "not provided" from "Active"
        chat.Status = body.Status;

        // Update metadata if provided
        if (body.Metadata != null && body.Metadata is System.Collections.Generic.IDictionary<string, object> metadataDict)
        {
            foreach (var kvp in metadataDict)
            {
                chat.Context[kvp.Key] = kvp.Value;
            }
        }

        chat.LastActivityAt = DateTime.UtcNow;

        await Task.Delay(0); // Keep async

        return new UpdateChatResponse
        {
            Chat = new Chat
            {
                Id = chatId,
                ChatId = chatId,
                ChatMessagesId = chat.ChatMessagesId,
                UserId = userId,
                Status = chat.Status,
                Summary = GenerateSummary(chat.History),
                MessageCount = chat.History.Count,
                CreatedAt = chat.CreatedAt,
                LastActivityAt = chat.LastActivityAt,
                Metadata = chat.Context.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                CreateUtcTick = chat.CreatedAt.Ticks,
                UpdateUtcTick = DateTime.UtcNow.Ticks
            }
        };
    }

    public async Task<ListChatsResponse> ListChatsAsync(ICallerInfo callerInfo, int? page, int? limit, ChatStatus? status)
    {
        var userId = callerInfo?.LzUserId ?? "unknown";
        var pageNumber = page ?? 1;
        var pageSize = Math.Min(limit ?? 50, 100); // Cap at 100 chats per page
        var skip = (pageNumber - 1) * pageSize;

        // Get all chats for this user
        var userChats = _chats.Values
            .Where(c => c.UserId == userId)
            .Where(c => !status.HasValue || c.Status == status.Value)
            .OrderByDescending(c => c.LastActivityAt)
            .ToList();

        var totalChats = userChats.Count;
        var chats = userChats
            .Skip(skip)
            .Take(pageSize)
            .Select(c => new Chat
            {
                Id = c.ChatId,
                ChatId = c.ChatId,
                UserId = c.UserId,
                Status = c.Status,
                CreatedAt = c.CreatedAt,
                LastActivityAt = c.LastActivityAt,
                Metadata = c.Context.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                CreateUtcTick = c.CreatedAt.Ticks,
                UpdateUtcTick = c.LastActivityAt.Ticks
            })
            .ToList();

        await Task.Delay(0); // Keep async

        return new ListChatsResponse
        {
            Chats = chats,
            Pagination = new ChatPaginationInfo
            {
                Page = pageNumber,
                Limit = pageSize,
                TotalChats = totalChats,
                HasMore = totalChats > skip + pageSize
            }
        };
    }

    /// <summary>
    /// Gets the keep-alive semaphore for a chat (used by internal keep-alive endpoint).
    /// </summary>
    public SemaphoreSlim? GetKeepAliveSemaphore(string chatId)
    {
        _keepAliveSemaphores.TryGetValue(chatId, out var semaphore);
        return semaphore;
    }

    /// <summary>
    /// Initiates a long-polling HTTP request to ourselves to represent this chat as active load.
    /// This prevents App Runner from scaling down the instance while background processing is active.
    /// </summary>
    private async Task InitiateKeepAliveAsync(string chatId)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("KeepAlive");

            _logger.LogDebug("Initiating keep-alive request for chat {ChatId}", chatId);

            // Long-polling request to our own internal endpoint
            var response = await httpClient.PostAsync(
                $"http://localhost:8080/ChatModule/internal/keepalive/{chatId}",
                null
            );

            _logger.LogDebug("Keep-alive request completed for chat {ChatId} with status {StatusCode}",
                chatId, response.StatusCode);
        }
        catch (TaskCanceledException)
        {
            _logger.LogDebug("Keep-alive request cancelled for chat {ChatId}", chatId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keep-alive request failed for chat {ChatId}", chatId);
        }
    }

    private async Task ProcessChatMessagesAsync(ConnectionChat chat)
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

                    // Publish user message event
                    await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
                    {
                        EventType = ChatEventType.Message_received,
                        ChatId = chat.ChatId,
                        Timestamp = DateTime.UtcNow,
                        Data = message
                    });

                    // Process with Bedrock LLM
                    var assistantResponse = await _llmClient.GenerateResponseAsync(chat.History);

                    // Create assistant message
                    var assistantMessageId = Guid.NewGuid().ToString();
                    var assistantTimestamp = DateTime.UtcNow;
                    var assistantMessage = new ChatMessage
                    {
                        MessageId = assistantMessageId,
                        ChatId = chat.ChatId,
                        Role = ChatMessageRole.Assistant,
                        Content = assistantResponse,
                        Timestamp = assistantTimestamp
                    };

                    // Add to session history
                    chat.History.Add(assistantMessage);
                    chat.LastActivityAt = DateTime.UtcNow;
                    chat.Status = ChatStatus.Active;

                    // Publish assistant response event
                    await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
                    {
                        EventType = ChatEventType.Message_completed,
                        ChatId = chat.ChatId,
                        Timestamp = DateTime.UtcNow,
                        Data = assistantMessage
                    });

                    _logger.LogInformation("Processed message for chat {ChatId}", chat.ChatId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message for chat {ChatId}", chat.ChatId);

                    // Publish error event
                    await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
                    {
                        EventType = ChatEventType.Error_occurred,
                        ChatId = chat.ChatId,
                        Timestamp = DateTime.UtcNow,
                        Data = new { Error = ex.Message }
                    });

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

            // Release keep-alive semaphore to complete the long-polling request
            if (_keepAliveSemaphores.TryRemove(chatId, out var semaphore))
            {
                try
                {
                    semaphore.Release();
                    semaphore.Dispose();
                    _logger.LogDebug("Released keep-alive semaphore for chat {ChatId}", chatId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error releasing keep-alive semaphore for chat {ChatId}", chatId);
                }
            }

            chat.CancellationToken.Dispose();
            _logger.LogInformation("Closed chat {ChatId}", chatId);
        }
    }

    private void CleanupExpiredChats(object state)
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

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SessionManager started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SessionManager stopping...");

        _cancellationTokenSource.Cancel();
        _cleanupTimer?.Dispose();

        // Close all sessions
        var allChatIds = _chats.Keys.ToList();
        await Task.WhenAll(allChatIds.Select(CloseChatInternalAsync));

        _cancellationTokenSource.Dispose();
        _logger.LogInformation("SessionManager stopped");
    }

    /// <summary>
    /// Generate a brief summary of the conversation
    /// </summary>
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
}

/// <summary>
/// Represents an active chat with in-memory state
/// </summary>
public class ConnectionChat
{
    public string ChatId { get; set; } = string.Empty;
    public string ChatMessagesId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public ChatStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public Channel<ChatMessage> MessageQueue { get; set; } = null!;
    public CancellationTokenSource CancellationToken { get; set; } = null!;
    public Dictionary<string, object> Context { get; set; } = new();
    public List<ChatMessage> History { get; set; } = new();
}
