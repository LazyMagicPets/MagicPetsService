# Implementation Plan: Event Publisher Abstraction

## Overview

Refactor the event publishing system to introduce a stronger abstraction layer between chat processing logic and WebSocket event publishing. This enables platform-specific implementations while maintaining clean separation of concerns.

## Current State Analysis

### Current Coupling Issues

1. **Direct Dependency on AWS AppSync**:
   - Interface named `IAppSyncEventPublisher` couples the abstraction to AWS
   - `ChatManagerService` directly depends on `IAppSyncEventPublisher`
   - Chat event construction (ChatEvent objects) happens in `ChatManagerService`

2. **Business Logic Mixed with Event Publishing**:
   - `ChatManagerService` knows about:
     - Event types (ChatEventType enum)
     - Event payload structure (ChatEvent class)
     - When to publish events (5 different publish calls scattered through processing)

3. **Limited Reusability**:
   - Cannot easily swap AWS AppSync for other WebSocket providers
   - Mock implementation duplicates event construction logic
   - Platform-specific implementations require knowledge of ChatEvent structure

### Current Architecture

```
ChatManagerService
    |
    ├─> IAppSyncEventPublisher (interface)
    |       ├─> AppSyncEventPublisher (AWS AppSync implementation)
    |       └─> MockAppSyncEventPublisher (test implementation)
    |
    └─> Constructs ChatEvent objects directly
        └─> Calls _eventPublisher.PublishChatEventAsync()
```

### Files Involved

1. **Interfaces**:
   - `IAppSyncEventPublisher.cs` - Current interface

2. **Implementations**:
   - `AppSyncEventPublisher.cs` - AWS AppSync implementation (156 lines)
   - `MockAppSyncEventPublisher.cs` - Test mock (141 lines)

3. **Consumers**:
   - `ChatManagerService.cs` - Main consumer (5 publish calls)
   - `ServiceRepoExtensions.cs` - DI registration

4. **Tests**:
   - `ChatModuleTestFixture.cs` - Uses mock

## Proposed Architecture

### New Layered Design

```
ChatManagerService
    |
    ├─> IChatEventPublisher (domain-level interface)
    |       |
    |       ├─> ChatEventPublisher (domain implementation)
    |       |       |
    |       |       └─> IWsEventPublisher (transport interface)
    |       |               ├─> AppSyncWsEventPublisher (AWS)
    |       |               ├─> SignalRWsEventPublisher (future: Azure)
    |       |               └─> MockWsEventPublisher (testing)
    |       |
    |       └─> MockChatEventPublisher (testing)
    |
    └─> Simple method calls: PublishUserMessage(), PublishStreamingChunk(), etc.
```

### Key Principles

1. **Domain Layer** (`IChatEventPublisher`):
   - Knows about: Chat domain concepts (messages, status, errors)
   - Doesn't know about: WebSocket protocols, AWS AppSync, event serialization
   - Provides high-level methods: `PublishUserMessageAsync()`, `PublishStreamingChunkAsync()`

2. **Transport Layer** (`IWsEventPublisher`):
   - Knows about: WebSocket event protocols, serialization, authentication
   - Doesn't know about: Chat domain logic, when to publish events
   - Provides low-level methods: `PublishEventAsync<T>(channel, eventType, data)`

3. **Separation of Concerns**:
   - ChatManagerService: Business logic only
   - ChatEventPublisher: Event construction and orchestration
   - WsEventPublisher: Platform-specific transport

## Implementation Plan

### Phase 1: Create New Interfaces

#### 1.1 Create IWsEventPublisher (Transport Layer)

**File**: `Service/Schemas/ChatSchemaRepo/Services/IWsEventPublisher.cs`

```csharp
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
```

**Rationale**:
- Generic interface works with any WebSocket platform
- Channel-based routing (common pattern across platforms)
- Metadata dictionary for extensibility
- No AWS-specific concepts

#### 1.2 Create IChatEventPublisher (Domain Layer)

**File**: `Service/Schemas/ChatSchemaRepo/Services/IChatEventPublisher.cs`

```csharp
namespace ChatSchemaRepo;

/// <summary>
/// Domain-level chat event publisher interface.
/// Provides high-level methods for publishing chat-related events.
/// </summary>
public interface IChatEventPublisher
{
    /// <summary>
    /// Publishes user message received event
    /// </summary>
    Task PublishUserMessageAsync(string chatId, ChatMessage message);

    /// <summary>
    /// Publishes assistant processing started event
    /// </summary>
    Task PublishProcessingStartedAsync(string chatId, string messageId);

    /// <summary>
    /// Publishes streaming chunk event
    /// </summary>
    Task PublishStreamingChunkAsync(string chatId, string messageId, string chunk);

    /// <summary>
    /// Publishes assistant message completed event
    /// </summary>
    Task PublishMessageCompletedAsync(string chatId, ChatMessage message);

    /// <summary>
    /// Publishes error event
    /// </summary>
    Task PublishErrorAsync(string chatId, string error);

    /// <summary>
    /// Publishes chat status changed event
    /// </summary>
    Task PublishStatusChangedAsync(string chatId, ChatStatus status);
}
```

**Rationale**:
- Methods match business events, not technical events
- Clear intent for each publish operation
- No ChatEvent construction in consumer
- Easy to mock for testing

### Phase 2: Implement Transport Layer

#### 2.1 Rename and Refactor AppSyncEventPublisher

**File**: `Service/Schemas/ChatSchemaRepo/Services/AppSyncWsEventPublisher.cs` (renamed)

**Changes**:
1. Rename class: `AppSyncEventPublisher` → `AppSyncWsEventPublisher`
2. Implement `IWsEventPublisher` instead of `IAppSyncEventPublisher`
3. Remove domain-specific methods (`PublishMessageEventAsync`, etc.)
4. Keep low-level AppSync protocol logic
5. Update DI registration

**Before**:
```csharp
public class AppSyncEventPublisher : IAppSyncEventPublisher
{
    public Task PublishChatEventAsync(string chatId, ChatEvent sessionEvent) { ... }
    public Task PublishMessageEventAsync(string chatId, ChatMessage message) { ... }
    public Task PublishChatStatusEventAsync(string chatId, ChatStatus status) { ... }
    public Task PublishErrorEventAsync(string chatId, string error) { ... }
}
```

**After**:
```csharp
public class AppSyncWsEventPublisher : IWsEventPublisher
{
    public Task PublishAsync<T>(
        string channel,
        string eventType,
        T data,
        Dictionary<string, object>? metadata = null)
    {
        // Extract dataType from metadata or infer from T
        var dataTypeName = metadata?.GetValueOrDefault("dataType")?.ToString()
            ?? GetDataTypeName<T>();

        // Build event payload
        var eventPayload = new
        {
            chatId = ExtractChatIdFromChannel(channel),
            eventType = eventType,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = data,
            dataType = dataTypeName
        };

        // Existing AppSync HTTP publishing logic
        return PublishEventAsync(httpDomain, channel, eventPayload, eventType);
    }

    private string GetDataTypeName<T>()
    {
        var type = typeof(T);
        if (type.Name.Contains("AnonymousType") || type.Name.Contains("<>"))
            return "Object";
        return type.Name;
    }

    private string ExtractChatIdFromChannel(string channel)
    {
        // Extract chatId from "/chat/{chatId}" pattern
        var parts = channel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    // Keep existing private methods: PublishEventAsync(), HTTP setup, etc.
}
```

#### 2.2 Create MockWsEventPublisher

**File**: `Service/TestModules/MockWsEventPublisher.cs`

```csharp
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
        Dictionary<string, object>? metadata = null)
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

    public IReadOnlyList<WsEvent> GetEvents(string channel)
    {
        return _eventsByChannel.TryGetValue(channel, out var events)
            ? events.AsReadOnly()
            : Array.Empty<WsEvent>();
    }

    public void Clear() => _eventsByChannel.Clear();
}

public class WsEvent
{
    public string Channel { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public object? Data { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
```

### Phase 3: Implement Domain Layer

#### 3.1 Create ChatEventPublisher

**File**: `Service/Schemas/ChatSchemaRepo/Services/ChatEventPublisher.cs`

```csharp
namespace ChatSchemaRepo;

/// <summary>
/// Domain-level chat event publisher implementation.
/// Translates high-level chat events to WebSocket transport events.
/// </summary>
public class ChatEventPublisher : IChatEventPublisher
{
    private readonly IWsEventPublisher _wsPublisher;
    private readonly ILogger<ChatEventPublisher> _logger;

    public ChatEventPublisher(
        IWsEventPublisher wsPublisher,
        ILogger<ChatEventPublisher> logger)
    {
        _wsPublisher = wsPublisher;
        _logger = logger;
    }

    public async Task PublishUserMessageAsync(string chatId, ChatMessage message)
    {
        _logger.LogInformation("Publishing user message for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_received.ToString(),
            data: message,
            metadata: new Dictionary<string, object>
            {
                { "dataType", nameof(ChatMessage) }
            });
    }

    public async Task PublishProcessingStartedAsync(string chatId, string messageId)
    {
        _logger.LogInformation("Publishing processing started for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_processing.ToString(),
            data: new { MessageId = messageId },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "Object" }
            });
    }

    public async Task PublishStreamingChunkAsync(string chatId, string messageId, string chunk)
    {
        _logger.LogDebug("Publishing streaming chunk for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_streaming.ToString(),
            data: new { MessageId = messageId, Chunk = chunk },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "Object" }
            });
    }

    public async Task PublishMessageCompletedAsync(string chatId, ChatMessage message)
    {
        _logger.LogInformation("Publishing message completed for chat {ChatId}", chatId);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_completed.ToString(),
            data: message,
            metadata: new Dictionary<string, object>
            {
                { "dataType", nameof(ChatMessage) }
            });
    }

    public async Task PublishErrorAsync(string chatId, string error)
    {
        _logger.LogWarning("Publishing error for chat {ChatId}: {Error}", chatId, error);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Error_occurred.ToString(),
            data: new { Error = error },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "Object" }
            });
    }

    public async Task PublishStatusChangedAsync(string chatId, ChatStatus status)
    {
        _logger.LogInformation("Publishing status changed for chat {ChatId}: {Status}", chatId, status);

        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Chat_status_changed.ToString(),
            data: new { Status = status.ToString() },
            metadata: new Dictionary<string, object>
            {
                { "dataType", "Object" }
            });
    }
}
```

#### 3.2 Create MockChatEventPublisher (Optional)

**File**: `Service/TestModules/MockChatEventPublisher.cs`

For tests that need to verify domain events without WebSocket transport details.

```csharp
namespace TestModules;

public class MockChatEventPublisher : IChatEventPublisher
{
    public List<string> PublishedEvents { get; } = new();

    public Task PublishUserMessageAsync(string chatId, ChatMessage message)
    {
        PublishedEvents.Add($"UserMessage:{chatId}:{message.MessageId}");
        return Task.CompletedTask;
    }

    public Task PublishProcessingStartedAsync(string chatId, string messageId)
    {
        PublishedEvents.Add($"ProcessingStarted:{chatId}:{messageId}");
        return Task.CompletedTask;
    }

    public Task PublishStreamingChunkAsync(string chatId, string messageId, string chunk)
    {
        PublishedEvents.Add($"StreamingChunk:{chatId}:{messageId}");
        return Task.CompletedTask;
    }

    public Task PublishMessageCompletedAsync(string chatId, ChatMessage message)
    {
        PublishedEvents.Add($"MessageCompleted:{chatId}:{message.MessageId}");
        return Task.CompletedTask;
    }

    public Task PublishErrorAsync(string chatId, string error)
    {
        PublishedEvents.Add($"Error:{chatId}");
        return Task.CompletedTask;
    }

    public Task PublishStatusChangedAsync(string chatId, ChatStatus status)
    {
        PublishedEvents.Add($"StatusChanged:{chatId}:{status}");
        return Task.CompletedTask;
    }

    public void Clear() => PublishedEvents.Clear();
}
```

### Phase 4: Update Consumer

#### 4.1 Refactor ChatManagerService

**File**: `Service/Schemas/ChatSchemaRepo/Services/ChatManagerService.cs`

**Changes**:

1. **Update constructor injection**:
```csharp
// Before:
private readonly IAppSyncEventPublisher _eventPublisher;

public ChatManagerService(
    ...,
    IAppSyncEventPublisher eventPublisher,
    ...)

// After:
private readonly IChatEventPublisher _eventPublisher;

public ChatManagerService(
    ...,
    IChatEventPublisher eventPublisher,
    ...)
```

2. **Simplify event publishing calls**:

```csharp
// Before (line 390):
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Message_received,
    ChatId = chat.ChatId,
    Timestamp = DateTime.UtcNow,
    Data = message
});

// After:
await _eventPublisher.PublishUserMessageAsync(chat.ChatId, message);
```

```csharp
// Before (line 404):
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Message_processing,
    ChatId = chat.ChatId,
    Timestamp = streamStartTime,
    Data = new { MessageId = assistantMessageId }
});

// After:
await _eventPublisher.PublishProcessingStartedAsync(chat.ChatId, assistantMessageId);
```

```csharp
// Before (line 419):
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Message_streaming,
    ChatId = chat.ChatId,
    Timestamp = DateTime.UtcNow,
    Data = new { MessageId = assistantMessageId, Chunk = textChunk }
});

// After:
await _eventPublisher.PublishStreamingChunkAsync(chat.ChatId, assistantMessageId, textChunk);
```

```csharp
// Before (line 449):
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Message_completed,
    ChatId = chat.ChatId,
    Timestamp = DateTime.UtcNow,
    Data = assistantMessage
});

// After:
await _eventPublisher.PublishMessageCompletedAsync(chat.ChatId, assistantMessage);
```

```csharp
// Before (line 464):
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Error_occurred,
    ChatId = chat.ChatId,
    Timestamp = DateTime.UtcNow,
    Data = new { Error = ex.Message }
});

// After:
await _eventPublisher.PublishErrorAsync(chat.ChatId, ex.Message);
```

**Benefits**:
- ChatManagerService no longer constructs ChatEvent objects
- Clear intent with named methods
- Simpler code (1 line vs 6 lines per publish)
- Easy to add/modify events without touching ChatManagerService

### Phase 5: Update Dependency Injection

#### 5.1 Update ServiceRepoExtensions

**File**: `Service/Schemas/ChatSchemaRepo/ServiceRepoExtensions.cs`

```csharp
// Before:
services.AddSingleton<IAppSyncEventPublisher, AppSyncEventPublisher>();

// After:
// Register transport layer
services.AddSingleton<IWsEventPublisher, AppSyncWsEventPublisher>();

// Register domain layer
services.AddSingleton<IChatEventPublisher, ChatEventPublisher>();
```

#### 5.2 Update Test Setup

**File**: `Service/TestModules/ChatModuleTestFixture.cs`

```csharp
// Before:
var mockEventPublisher = new MockAppSyncEventPublisher();
services.AddSingleton<IAppSyncEventPublisher>(mockEventPublisher);

// After - Option 1: Full stack
var mockWsPublisher = new MockWsEventPublisher();
services.AddSingleton<IWsEventPublisher>(mockWsPublisher);
services.AddSingleton<IChatEventPublisher, ChatEventPublisher>();

// After - Option 2: Mock domain layer
var mockChatPublisher = new MockChatEventPublisher();
services.AddSingleton<IChatEventPublisher>(mockChatPublisher);
```

### Phase 6: Deprecation and Cleanup

#### 6.1 Mark Old Interface as Obsolete

**File**: `Service/Schemas/ChatSchemaRepo/Services/IAppSyncEventPublisher.cs`

```csharp
[Obsolete("Use IChatEventPublisher for domain events or IWsEventPublisher for transport. Will be removed in v2.0")]
public interface IAppSyncEventPublisher
{
    // ... existing methods
}
```

#### 6.2 Keep Old Implementation Temporarily

Keep `AppSyncEventPublisher.cs` alongside `AppSyncWsEventPublisher.cs` for one release cycle to allow gradual migration.

#### 6.3 Remove in Future Release

After verification period:
1. Delete `IAppSyncEventPublisher.cs`
2. Delete `AppSyncEventPublisher.cs`
3. Delete `MockAppSyncEventPublisher.cs`
4. Remove obsolete DI registrations

## Benefits of New Architecture

### 1. **Separation of Concerns**
- ChatManagerService: Pure business logic
- ChatEventPublisher: Event orchestration
- WsEventPublisher: Platform-specific transport

### 2. **Platform Independence**
- Easy to add SignalR, WebSockets, or other platforms
- No AWS-specific code in domain layer
- Transport details isolated

### 3. **Improved Testability**
- Mock at domain level (MockChatEventPublisher) for business logic tests
- Mock at transport level (MockWsEventPublisher) for integration tests
- Clear test intent with named methods

### 4. **Better Maintainability**
- Event construction logic in one place
- Adding new events doesn't require touching ChatManagerService
- Clear layering makes code easier to understand

### 5. **Simplified Consumer Code**
```csharp
// Before: 6 lines
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Message_received,
    ChatId = chat.ChatId,
    Timestamp = DateTime.UtcNow,
    Data = message
});

// After: 1 line
await _eventPublisher.PublishUserMessageAsync(chat.ChatId, message);
```

### 6. **Extensibility**
- Add new event types without changing interfaces
- Add metadata/tags to events easily
- Support multiple transports simultaneously (e.g., AppSync + logging)

## Migration Strategy

### Step 1: Add New Code (No Breaking Changes)
- Create `IWsEventPublisher` interface
- Create `IChatEventPublisher` interface
- Create `AppSyncWsEventPublisher` implementation
- Create `ChatEventPublisher` implementation
- Create mock implementations
- Add new DI registrations

### Step 2: Update Consumers
- Update ChatManagerService to use `IChatEventPublisher`
- Simplify publish calls
- Update tests

### Step 3: Verify
- Run all tests
- Test locally with LocalWebService
- Verify events still flow correctly
- Check client still receives events

### Step 4: Mark Old Code Obsolete
- Add `[Obsolete]` attributes
- Document migration path
- Keep old code for one release

### Step 5: Remove Old Code
- After verification period (1-2 releases)
- Delete obsolete interfaces and implementations
- Update documentation

## Files to Create

1. ✅ `Service/Schemas/ChatSchemaRepo/Services/IWsEventPublisher.cs` (new interface)
2. ✅ `Service/Schemas/ChatSchemaRepo/Services/IChatEventPublisher.cs` (new interface)
3. ✅ `Service/Schemas/ChatSchemaRepo/Services/AppSyncWsEventPublisher.cs` (refactored from AppSyncEventPublisher)
4. ✅ `Service/Schemas/ChatSchemaRepo/Services/ChatEventPublisher.cs` (new implementation)
5. ✅ `Service/TestModules/MockWsEventPublisher.cs` (new mock)
6. ✅ `Service/TestModules/MockChatEventPublisher.cs` (new mock)

## Files to Modify

1. ✅ `Service/Schemas/ChatSchemaRepo/Services/ChatManagerService.cs`
2. ✅ `Service/Schemas/ChatSchemaRepo/ServiceRepoExtensions.cs`
3. ✅ `Service/TestModules/ChatModuleTestFixture.cs`

## Files to Deprecate (Keep Temporarily)

1. ⏳ `Service/Schemas/ChatSchemaRepo/Services/IAppSyncEventPublisher.cs` (mark obsolete)
2. ⏳ `Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs` (mark obsolete)
3. ⏳ `Service/TestModules/MockAppSyncEventPublisher.cs` (mark obsolete)

## Files to Delete (Future Release)

1. 🗑️ `Service/Schemas/ChatSchemaRepo/Services/IAppSyncEventPublisher.cs`
2. 🗑️ `Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs`
3. 🗑️ `Service/TestModules/MockAppSyncEventPublisher.cs`

## Testing Plan

### Unit Tests
- Test ChatEventPublisher with MockWsEventPublisher
- Verify correct channel paths
- Verify correct event types
- Verify metadata is set correctly

### Integration Tests
- Test ChatManagerService with ChatEventPublisher + MockWsEventPublisher
- Verify all 5 event types are published
- Verify event order
- Verify event data

### End-to-End Tests
- Test with AppSyncWsEventPublisher
- Verify LocalWebService works
- Verify client receives events
- Verify event structure matches client expectations

## Future Enhancements

### 1. Multiple Transport Support
```csharp
public class MultiTransportWsEventPublisher : IWsEventPublisher
{
    private readonly IEnumerable<IWsEventPublisher> _publishers;

    public async Task PublishAsync<T>(...)
    {
        await Task.WhenAll(_publishers.Select(p => p.PublishAsync(...)));
    }
}
```

### 2. Event Filtering/Routing
```csharp
public class FilteredChatEventPublisher : IChatEventPublisher
{
    private readonly IChatEventPublisher _inner;
    private readonly Func<string, bool> _filter;

    public Task PublishUserMessageAsync(string chatId, ChatMessage message)
    {
        return _filter(chatId) ? _inner.PublishUserMessageAsync(chatId, message) : Task.CompletedTask;
    }
}
```

### 3. Event Middleware
```csharp
public interface IEventMiddleware
{
    Task OnPublishingAsync(string channel, string eventType, object data);
}
```

## Timeline Estimate

- **Phase 1** (Interfaces): 1 hour
- **Phase 2** (Transport Layer): 2 hours
- **Phase 3** (Domain Layer): 2 hours
- **Phase 4** (Update Consumer): 1 hour
- **Phase 5** (DI Updates): 30 minutes
- **Phase 6** (Testing): 2 hours

**Total: ~8-9 hours of development work**

## Success Criteria

✅ All tests pass
✅ ChatManagerService simplified (5 publish calls → 5 simple method calls)
✅ No AWS-specific code in ChatManagerService
✅ LocalWebService works with new architecture
✅ Client still receives events correctly
✅ Easy to add new transport implementations
✅ Clear separation between domain and transport layers

---

**Ready for Implementation?** Please review this plan and provide feedback before proceeding.
