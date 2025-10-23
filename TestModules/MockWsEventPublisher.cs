using System.Collections.Concurrent;
using ChatSchemaRepo;

namespace TestModules;

/// <summary>
/// Mock WebSocket event publisher for testing
/// </summary>
public class MockWsEventPublisher : IWsEventPublisher
{
    private readonly ConcurrentDictionary<string, List<WsEvent>> _eventsByChannel = new();

    public Task PublishAsync<T>(
        string channel,
        string eventType,
        T data,
        Dictionary<string, object>? metadata = null,
        ICallerInfo? callerInfo = null)
    {
        var wsEvent = new WsEvent
        {
            Channel = channel,
            EventType = eventType,
            Data = data,
            Metadata = metadata ?? new(),
            Timestamp = DateTime.UtcNow
        };

        _eventsByChannel.AddOrUpdate(
            channel,
            _ => new List<WsEvent> { wsEvent },
            (_, events) =>
            {
                lock (events) { events.Add(wsEvent); }
                return events;
            });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets all events published to a channel
    /// </summary>
    public IReadOnlyList<WsEvent> GetEvents(string channel)
    {
        return _eventsByChannel.TryGetValue(channel, out var events)
            ? events.AsReadOnly()
            : Array.Empty<WsEvent>();
    }

    /// <summary>
    /// Waits for a specific event type on a channel
    /// </summary>
    public async Task<WsEvent> WaitForEventAsync(string channel, string eventType, TimeSpan timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        var startTime = DateTime.UtcNow;

        while (!cts.Token.IsCancellationRequested)
        {
            var events = GetEvents(channel);
            var matchingEvent = events.FirstOrDefault(e => e.EventType == eventType);
            if (matchingEvent != null)
            {
                return matchingEvent;
            }

            await Task.Delay(50, cts.Token);
        }

        throw new TimeoutException($"Timeout waiting for event {eventType} on channel {channel}");
    }

    /// <summary>
    /// Waits for a specific event type on a channel (with default 10 second timeout)
    /// </summary>
    public Task<WsEvent> WaitForEventAsync(string channel, string eventType)
    {
        return WaitForEventAsync(channel, eventType, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Clears all stored events (for cleanup between tests)
    /// </summary>
    public void Clear() => _eventsByChannel.Clear();
}

/// <summary>
/// Represents a WebSocket event for testing
/// </summary>
public class WsEvent
{
    public string Channel { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public object? Data { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
