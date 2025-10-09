using ChatSchema;
using ChatSchemaRepo;
using System.Collections.Concurrent;

namespace TestModules;

/// <summary>
/// Mock implementation of IAppSyncEventPublisher for testing.
/// Allows tests to wait for specific events instead of using fixed delays.
/// </summary>
public class MockAppSyncEventPublisher : IAppSyncEventPublisher
{
    private readonly ConcurrentDictionary<string, List<ChatEvent>> _eventsByChatId = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ChatEvent>> _eventWaiters = new();

    /// <summary>
    /// Gets all events published for a specific chat
    /// </summary>
    public IReadOnlyList<ChatEvent> GetEvents(string chatId)
    {
        if (_eventsByChatId.TryGetValue(chatId, out var events))
        {
            return events.AsReadOnly();
        }
        return Array.Empty<ChatEvent>();
    }

    /// <summary>
    /// Waits for a specific event type to be published for a chat
    /// </summary>
    public async Task<ChatEvent> WaitForEventAsync(string chatId, ChatEventType eventType, TimeSpan timeout)
    {
        var key = $"{chatId}:{eventType}";
        var tcs = _eventWaiters.GetOrAdd(key, _ => new TaskCompletionSource<ChatEvent>());

        // Check if event already exists
        if (_eventsByChatId.TryGetValue(chatId, out var events))
        {
            var existingEvent = events.FirstOrDefault(e => e.EventType == eventType);
            if (existingEvent != null)
            {
                return existingEvent;
            }
        }

        // Wait for event with timeout
        using var cts = new CancellationTokenSource(timeout);
        using var registration = cts.Token.Register(() => tcs.TrySetCanceled());

        try
        {
            return await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            throw new TimeoutException($"Timeout waiting for event {eventType} on chat {chatId}");
        }
    }

    /// <summary>
    /// Waits for a specific event type to be published for a chat (with default 10 second timeout)
    /// </summary>
    public Task<ChatEvent> WaitForEventAsync(string chatId, ChatEventType eventType)
    {
        return WaitForEventAsync(chatId, eventType, TimeSpan.FromSeconds(10));
    }

    public Task PublishChatEventAsync(string chatId, ChatEvent sessionEvent)
    {
        // Store event
        _eventsByChatId.AddOrUpdate(
            chatId,
            _ => new List<ChatEvent> { sessionEvent },
            (_, events) =>
            {
                lock (events)
                {
                    events.Add(sessionEvent);
                }
                return events;
            });

        // Complete any waiters for this event type
        var key = $"{chatId}:{sessionEvent.EventType}";
        if (_eventWaiters.TryRemove(key, out var tcs))
        {
            tcs.TrySetResult(sessionEvent);
        }

        return Task.CompletedTask;
    }

    public Task PublishMessageEventAsync(string chatId, ChatMessage message)
    {
        var sessionEvent = new ChatEvent
        {
            EventType = message.Role == ChatMessageRole.User ? ChatEventType.Message_received : ChatEventType.Message_completed,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow,
            Data = message
        };

        return PublishChatEventAsync(chatId, sessionEvent);
    }

    public Task PublishChatStatusEventAsync(string chatId, ChatStatus status)
    {
        var sessionEvent = new ChatEvent
        {
            EventType = ChatEventType.Chat_status_changed,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow,
            Data = new { Status = status.ToString() }
        };

        return PublishChatEventAsync(chatId, sessionEvent);
    }

    public Task PublishErrorEventAsync(string chatId, string error)
    {
        var sessionEvent = new ChatEvent
        {
            EventType = ChatEventType.Error_occurred,
            ChatId = chatId,
            Timestamp = DateTime.UtcNow,
            Data = new { Error = error }
        };

        return PublishChatEventAsync(chatId, sessionEvent);
    }

    /// <summary>
    /// Clears all stored events (for cleanup between tests)
    /// </summary>
    public void Clear()
    {
        _eventsByChatId.Clear();
        _eventWaiters.Clear();
    }
}
