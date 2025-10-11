namespace ChatSchemaRepo;

/// <summary>
/// Platform-agnostic WebSocket event publisher interface.
/// Implementations handle platform-specific transport (AWS AppSync, SignalR, etc.)
/// </summary>
public interface IWsEventPublisher
{
    /// <summary>
    /// Publishes a typed event to a channel
    /// </summary>
    /// <typeparam name="T">Event data type</typeparam>
    /// <param name="channel">Channel path (e.g., "/chat/{chatId}")</param>
    /// <param name="eventType">Event type identifier</param>
    /// <param name="data">Event data payload</param>
    /// <param name="metadata">Optional metadata (dataType, timestamp, etc.)</param>
    Task PublishAsync<T>(
        string channel,
        string eventType,
        T data,
        Dictionary<string, object>? metadata = null);
}
