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
    private readonly IAppSyncEventPublisher _eventPublisher;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMessagePersistence _messagePersistence;
    private readonly ConcurrentDictionary<string, ConnectionChat> _chats;
    private readonly ConcurrentDictionary<string, Task> _backgroundTasks;
    private readonly SemaphoreSlim _keepAliveSemaphore;
    private Task? _keepAliveTask;
    private readonly Timer _cleanupTimer;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public ChatManagerService(
        ILogger<ChatManagerService> logger,
        ILlmClient llmClient,
        IAppSyncEventPublisher eventPublisher,
        IHttpClientFactory httpClientFactory,
        IMessagePersistence messagePersistence)
    {
        _logger = logger;
        _llmClient = llmClient;
        _eventPublisher = eventPublisher;
        _httpClientFactory = httpClientFactory;
        _messagePersistence = messagePersistence;
        _chats = new ConcurrentDictionary<string, ConnectionChat>();
        _backgroundTasks = new ConcurrentDictionary<string, Task>();
        _keepAliveSemaphore = new SemaphoreSlim(0, 1);
        _cancellationTokenSource = new CancellationTokenSource();

        // Run cleanup every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredChats, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task<Chat> InitializeChatAsync(ICallerInfo callerInfo, Chat chat)
    {
        // Generate chatId if not provided (chatMessagesId is the same as chatId now)
        var chatId = chat.ChatId ?? Guid.NewGuid().ToString();
        var userId = callerInfo?.LzUserId ?? "unknown";

        // Create in-memory chat state
        var connectionChat = new ConnectionChat
        {
            ChatId = chatId,
            ChatMessagesId = chatId, // Same as chatId
            UserId = userId,
            Status = ChatStatus.Active,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            MessageQueue = Channel.CreateUnbounded<ChatMessage>(),
            CancellationToken = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token),
            Context = chat.Metadata != null && chat.Metadata is IDictionary<string, object> metadataDict
                ? new Dictionary<string, object>(metadataDict)
                : new Dictionary<string, object>(),
            History = new List<ChatMessage>(),
            CallerInfo = callerInfo // Store for service host resolution
        };

        _chats.TryAdd(chatId, connectionChat);

        // Start background processing task
        var backgroundTask = ProcessChatMessagesAsync(connectionChat);
        _backgroundTasks.TryAdd(chatId, backgroundTask);

        // Keep-alive feature disabled for now
        // if (_chats.Count == 1 && _keepAliveTask == null)
        // {
        //     _keepAliveTask = InitiateKeepAliveAsync();
        // }

        _logger.LogInformation("Initialized chat {ChatId} for user {UserId}", chatId, userId);

        var now = DateTime.UtcNow;
        var nowTicks = now.Ticks;

        await Task.Delay(0); // Keep async

        // Return enriched Chat object
        return new Chat
        {
            Id = chatId,
            ChatId = chatId,
            UserId = userId,
            Status = ChatStatus.Active,
            Summary = null,
            MessageCount = 0,
            CreatedAt = connectionChat.CreatedAt,
            LastActivityAt = connectionChat.LastActivityAt,
            Metadata = connectionChat.Context,
            CreateUtcTick = nowTicks,
            UpdateUtcTick = nowTicks
        };
    }

    public async Task<ChatMessage> ProcessUserMessageAsync(ICallerInfo callerInfo, string chatId, ChatMessage message)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        // Enrich message with IDs and timestamp
        var enrichedMessage = new ChatMessage
        {
            MessageId = message.MessageId ?? Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = ChatMessageRole.User,
            Content = message.Content,
            Timestamp = DateTime.UtcNow,
            Metadata = message.Metadata
        };

        // Add to history
        chat.History.Add(enrichedMessage);
        chat.LastActivityAt = DateTime.UtcNow;
        chat.Status = ChatStatus.Processing;

        // Queue for background processing
        await chat.MessageQueue.Writer.WriteAsync(enrichedMessage, chat.CancellationToken.Token);

        _logger.LogInformation("Queued message for chat {ChatId}", chatId);

        return enrichedMessage;
    }

    public async Task<Chat> GetChatByIdAsync(ICallerInfo callerInfo, string chatId)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        await Task.Delay(0); // Keep async

        return new Chat
        {
            Id = chatId,
            ChatId = chatId,
            UserId = userId,
            Status = chat.Status,
            Summary = GenerateSummary(chat.History),
            MessageCount = chat.History.Count,
            CreatedAt = chat.CreatedAt,
            LastActivityAt = chat.LastActivityAt,
            Metadata = chat.Context,
            CreateUtcTick = chat.CreatedAt.Ticks,
            UpdateUtcTick = chat.LastActivityAt.Ticks
        };
    }

    public async Task<List<ChatMessage>> GetChatHistoryAsync(ICallerInfo callerInfo, string chatId, int? page, int? limit)
    {
        var userId = callerInfo?.LzUserId ?? "unknown";

        // Check if chat is in memory (active)
        if (_chats.TryGetValue(chatId, out var chat))
        {
            // Verify ownership
            if (chat.UserId != userId)
            {
                throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
            }

            var pageNumber = page ?? 1;
            var pageSize = Math.Min(limit ?? 50, 100);
            var skip = (pageNumber - 1) * pageSize;

            return chat.History
                .OrderBy(m => m.Timestamp)
                .Skip(skip)
                .Take(pageSize)
                .ToList();
        }

        // Chat not in memory - load from DynamoDB
        var messages = await _messagePersistence.GetMessagesAsync(callerInfo, chatId);

        if (messages == null || messages.Count == 0)
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // TODO: Add ownership verification by loading Chat record from DynamoDB
        // For now, we assume if messages exist, they belong to the caller

        var pageNum = page ?? 1;
        var pageSz = Math.Min(limit ?? 50, 100);
        var skipCount = (pageNum - 1) * pageSz;

        return messages
            .OrderBy(m => m.Timestamp)
            .Skip(skipCount)
            .Take(pageSz)
            .ToList();
    }

    public async Task<Chat> UpdateChatAsync(ICallerInfo callerInfo, Chat chat)
    {
        if (!_chats.TryGetValue(chat.ChatId!, out var connectionChat))
        {
            throw new InvalidOperationException($"Chat {chat.ChatId} not found");
        }

        // Verify ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (connectionChat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chat.ChatId}");
        }

        // Update in-memory state
        connectionChat.Status = chat.Status;

        if (chat.Metadata != null && chat.Metadata is IDictionary<string, object> metadata)
        {
            foreach (var kvp in metadata)
            {
                connectionChat.Context[kvp.Key] = kvp.Value;
            }
        }

        connectionChat.LastActivityAt = DateTime.UtcNow;

        await Task.Delay(0); // Keep async

        return new Chat
        {
            Id = chat.ChatId,
            ChatId = chat.ChatId,
            UserId = userId,
            Status = connectionChat.Status,
            Summary = GenerateSummary(connectionChat.History),
            MessageCount = connectionChat.History.Count,
            CreatedAt = connectionChat.CreatedAt,
            LastActivityAt = connectionChat.LastActivityAt,
            Metadata = connectionChat.Context,
            CreateUtcTick = connectionChat.CreatedAt.Ticks,
            UpdateUtcTick = DateTime.UtcNow.Ticks
        };
    }

    public async Task CloseChatAsync(ICallerInfo callerInfo, string chatId)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        await CloseChatInternalAsync(chatId);
    }

    public async Task PersistChatMessagesAsync(ICallerInfo callerInfo, string chatId)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            throw new InvalidOperationException($"Chat {chatId} not found");
        }

        // Verify ownership
        var userId = callerInfo?.LzUserId ?? "unknown";
        if (chat.UserId != userId)
        {
            throw new UnauthorizedAccessException($"User {userId} does not own chat {chatId}");
        }

        // Persist messages to DynamoDB without closing the chat
        await PersistChatHistoryAsync(chat.CallerInfo, chatId, chat.History);
        _logger.LogInformation("Persisted messages for chat {ChatId} (chat remains active)", chatId);
    }

    public SemaphoreSlim? GetKeepAliveSemaphore(string chatId)
    {
        // Return the shared semaphore if there are active chats
        return _chats.Count > 0 ? _keepAliveSemaphore : null;
    }

    // Private helper methods

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

                    // Process with LLM using streaming
                    var assistantMessageId = Guid.NewGuid().ToString();
                    var streamStartTime = DateTime.UtcNow;
                    var fullResponse = new System.Text.StringBuilder();

                    // Publish streaming start event
                    await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
                    {
                        EventType = ChatEventType.Message_processing,
                        ChatId = chat.ChatId,
                        Timestamp = streamStartTime,
                        Data = new { MessageId = assistantMessageId }
                    });

                    // Stream the response
                    await foreach (var textChunk in _llmClient.GenerateResponseStreamAsync(chat.History, chat.CancellationToken.Token))
                    {
                        fullResponse.Append(textChunk);

                        // Publish streaming chunk event with only the new chunk
                        // Client can accumulate chunks on their end
                        await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
                        {
                            EventType = ChatEventType.Message_streaming,
                            ChatId = chat.ChatId,
                            Timestamp = DateTime.UtcNow,
                            Data = new
                            {
                                MessageId = assistantMessageId,
                                Chunk = textChunk
                                // Removed FullContent to reduce bandwidth - client accumulates chunks
                            }
                        });
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
                    await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
                    {
                        EventType = ChatEventType.Message_completed,
                        ChatId = chat.ChatId,
                        Timestamp = DateTime.UtcNow,
                        Data = assistantMessage
                    });

                    _logger.LogInformation("Processed streaming message for chat {ChatId}, total length: {Length}", chat.ChatId, fullResponse.Length);
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
    /// Persists all messages in chat history to the ChatMessages table when chat closes.
    /// Creates or replaces the entire ChatMessages record with all messages at once.
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
    public ICallerInfo? CallerInfo { get; set; } // Stores caller info for service host resolution
}
