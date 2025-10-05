# ChatSchemaRepo

Repository and service layer for the Chat module, implementing all business logic for real-time AI conversations with Amazon Bedrock integration.

## Overview

ChatSchemaRepo contains the repository pattern implementation and all business logic for ChatModule. It follows the standard MagicPets architecture where controllers delegate to repositories, which in turn delegate to services for complex operations.

## Architecture

```
ChatModule/                           (Controller - Routing only)
  └── ChatModuleController.g.cs      Generated controller, delegates to repos

ChatSchemaRepo/                       (Repository & Service Layer)
  ├── Repos/
  │   ├── ChatRepo.cs                CRUD operations, orchestrates in-memory + DynamoDB
  │   └── ChatMessagesRepo.cs        Message operations
  ├── Services/
  │   ├── ChatManagerService.cs      In-memory state management + background processing
  │   ├── IChatManagerService.cs     Service interface for DI
  │   ├── BedrockChat.cs             Amazon Bedrock LLM integration
  │   ├── ILlmClient.cs              LLM abstraction interface
  │   └── AppSyncEventPublisher.cs   Real-time event publishing
  └── ServiceRepoExtensions.cs       DI registration for all services

ChatSchema/                           (DTOs - Generated)
  └── DTOs/
      ├── Chat.g.cs                  Chat entity (IItem for DynamoDB)
      ├── ChatMessages.g.cs          ChatMessages entity (separate table)
      ├── ChatMessage.g.cs           Message object
      └── ChatStatus.g.cs            Enum
```

## Key Design Principles

### 1. Hybrid In-Memory + DynamoDB Architecture

**The Problem**:
- Background AI processing requires in-memory state
- Container restarts would lose conversation history
- Need fast access for active chats, persistence for all chats

**The Solution**:
```
Active Chat Flow:
- ChatRepo.CreateAsync() → ChatManagerService.InitializeChatAsync() → In-memory state
- ChatRepo.CreateAsync() → base.CreateAsync() → DynamoDB persistence
- Messages processed in-memory for speed
- Messages persisted to DynamoDB for durability

Inactive Chat Flow:
- ChatRepo.ReadAsync() → Tries ChatManagerService first
- If not in memory → base.ReadAsync() → Loads from DynamoDB
- Auto-rehydration when chat is accessed again
```

### 2. Repository Pattern with Service Delegation

**ChatRepo** orchestrates between in-memory and persistent storage:
- **CreateAsync**: Initialize in-memory → Persist to DynamoDB
- **ReadAsync**: Try in-memory → Fall back to DynamoDB
- **UpdateAsync**: Update in-memory → Persist to DynamoDB
- **DeleteAsync**: Close in-memory → Delete from DynamoDB
- **ListAsync**: Return all from DynamoDB (active + inactive)

**ChatManagerService** handles complex business logic:
- In-memory state management (`ConcurrentDictionary<string, ConnectionChat>`)
- Background message processing (one Task per chat)
- Message queuing (Channel-based)
- Automatic cleanup (30-min timeout)
- Keep-alive coordination

### 3. Interface-Based Design for Testability

All services implement interfaces:
- **IChatManagerService**: Core business logic interface
- **ILlmClient**: LLM abstraction (allows swapping Bedrock, GPT-4, etc.)
- **IDocumentRepo<T>**: Repository interface from LazyMagic

Benefits:
- Easy mocking for unit tests
- Swappable LLM providers
- Dependency injection
- Loose coupling

## Components

### ChatRepo (`Repos/ChatRepo.cs`)

Extends `DYDBRepository<Chat>` to provide standard CRUD operations with in-memory delegation.

**Constructor**:
```csharp
public ChatRepo(
    IAmazonDynamoDB client,
    IChatManagerService chatManagerService,
    IChatMessagesRepo chatMessagesRepo
) : base(client)
```

**Key Methods**:

#### CreateAsync(callerInfo, chat)
1. Calls `ChatManagerService.InitializeChatAsync()` to create in-memory state
2. Calls `base.CreateAsync()` to persist Chat to DynamoDB
3. Creates empty ChatMessages record in DynamoDB
4. Returns initialized Chat object

#### ReadAsync(callerInfo, id)
1. Tries `ChatManagerService.GetChatByIdAsync()` (in-memory)
2. If `InvalidOperationException`: Falls back to `base.ReadAsync()` (DynamoDB)
3. Returns Chat object

#### UpdateAsync(callerInfo, chat, forceUpdate)
1. Tries `ChatManagerService.UpdateChatAsync()` (in-memory)
2. Calls `base.UpdateAsync()` to persist changes
3. If not in memory: Just updates DynamoDB
4. Returns updated Chat

#### DeleteAsync(callerInfo, id)
1. Tries `ChatManagerService.CloseChatAsync()` to cleanup in-memory state
2. Calls `base.DeleteAsync()` to remove from DynamoDB
3. Deletes associated ChatMessages record
4. Returns 200 OK

#### ListAsync(callerInfo, limit)
- Returns all chats from DynamoDB (both active and inactive)
- Includes pagination support

### ChatMessagesRepo (`Repos/ChatMessagesRepo.cs`)

Extends `DYDBRepository<ChatMessages>` to provide message-specific operations.

**Constructor**:
```csharp
public ChatMessagesRepo(
    IAmazonDynamoDB client,
    IChatManagerService chatManagerService
) : base(client)
```

**Key Methods**:

#### AddMessageAsync(callerInfo, chatId, message)
1. Calls `ChatManagerService.ProcessUserMessageAsync()` to queue message
2. Loads ChatMessages from DynamoDB
3. Appends message to Messages array
4. Calls `base.UpdateAsync()` to persist
5. Returns added ChatMessage

#### GetMessagesAsync(callerInfo, chatId, page, limit)
1. Tries `ChatManagerService.GetChatHistoryAsync()` (in-memory, if chat active)
2. If `InvalidOperationException`: Loads from DynamoDB
3. Applies pagination
4. Returns ICollection<ChatMessage>

### ChatManagerService (`Services/ChatManagerService.cs`)

**Type**: `IHostedService` (singleton background service)

Core service managing chat lifecycle, in-memory state, and background processing.

**State Management**:
```csharp
private readonly ConcurrentDictionary<string, ConnectionChat> _chats;
private readonly SemaphoreSlim _keepAliveSemaphore;
private Task? _keepAliveTask;
```

**ConnectionChat Internal Class**:
```csharp
public class ConnectionChat
{
    public string ChatId { get; set; }
    public string UserId { get; set; }
    public ChatStatus Status { get; set; }
    public List<ChatMessage> History { get; set; }
    public Channel<ChatMessage> MessageQueue { get; set; }
    public Task ProcessingTask { get; set; }
    public DateTime LastActivityAt { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; }
    public ICallerInfo? CallerInfo { get; set; }  // Stores Host header
}
```

**Key Methods**:

#### InitializeChatAsync(callerInfo, chat)
1. Generates unique chatId
2. Creates ConnectionChat with Channel queue
3. Starts background `ProcessChatMessagesAsync()` task
4. If first chat: Starts single keep-alive task
5. Stores in `_chats` dictionary
6. Returns initialized Chat object

#### ProcessUserMessageAsync(callerInfo, chatId, message)
1. Validates chat exists and user owns it
2. Sets messageId and timestamp
3. Adds to in-memory chat.History
4. Queues in chat.MessageQueue for background processing
5. Returns message immediately (async processing)

#### ProcessChatMessagesAsync(chat) - Background Task
Runs continuously for each chat:
1. Reads messages from chat.MessageQueue
2. Publishes "message_received" event via AppSync
3. Calls `ILlmClient.GenerateResponseAsync()` with conversation history
4. Creates assistant ChatMessage
5. Adds to chat.History
6. Updates Chat.Summary
7. Publishes "message_completed" event
8. Repeats until chat closed

#### GetChatByIdAsync(callerInfo, chatId)
- Returns chat from `_chats` dictionary
- Throws `InvalidOperationException` if not found (triggers DynamoDB fallback)

#### GetChatHistoryAsync(callerInfo, chatId, page, limit)
- Returns paginated messages from in-memory chat.History
- Throws `InvalidOperationException` if chat not in memory

#### UpdateChatAsync(callerInfo, chat)
- Updates in-memory ConnectionChat state
- Returns updated Chat object

#### CloseChatAsync(callerInfo, chatId)
1. Validates ownership
2. Cancels background processing task
3. Completes MessageQueue channel
4. Removes from `_chats` dictionary
5. If last chat: Releases keep-alive semaphore

#### CleanupExpiredChats() - Timer-Based
Runs every 5 minutes:
- Finds chats with `LastActivityAt > 30 minutes`
- Calls `CloseChatAsync()` for each expired chat
- Automatic resource cleanup

#### Keep-Alive Management

**Single Service-Wide Keep-Alive**:
- `_keepAliveSemaphore`: Shared SemaphoreSlim for all chats
- `InitiateKeepAliveAsync()`: Single HTTP POST to `/internal/keepalive`
- Blocks on semaphore until last chat closes
- Prevents AWS App Runner scale-down during processing

**Dynamic Host Resolution**:
```csharp
private string GetServiceHost()
{
    var firstChat = _chats.Values.FirstOrDefault();
    if (firstChat?.CallerInfo?.Headers != null &&
        firstChat.CallerInfo.Headers.TryGetValue("Host", out var host) &&
        !string.IsNullOrEmpty(host))
    {
        var scheme = host.StartsWith("localhost") ? "http" : "https";
        return $"{scheme}://{host}";
    }

    return "http://localhost:8080";  // Fallback
}
```

### BedrockChat (`Services/BedrockChat.cs`)

**Interface**: `ILlmClient`
**Type**: Singleton service

Amazon Bedrock integration for AI responses.

**Configuration**:
```csharp
private const string ModelId = "anthropic.claude-3-sonnet-20240229-v1:0";
```

**Key Methods**:

#### GenerateResponseAsync(conversationHistory)
1. Converts ChatMessage list to Bedrock message format
2. Calls `InvokeModelAsync()` with full conversation context
3. Parses JSON response
4. Returns assistant response text
5. On error: Returns friendly error message

#### GenerateResponseAsync(userMessage)
- Simplified overload for single message
- Creates single-message conversation
- Calls main GenerateResponseAsync()

**Implementation Notes**:
- Uses `IAmazonBedrockRuntime` SDK client
- Implements `ILlmClient` interface for swappability
- Error handling returns user-friendly messages
- Future: Can add streaming support via `InvokeModelWithResponseStreamAsync()`

### ILlmClient (`Services/ILlmClient.cs`)

Abstraction interface for LLM providers.

```csharp
public interface ILlmClient
{
    Task<string> GenerateResponseAsync(List<ChatMessage> conversationHistory);
    Task<string> GenerateResponseAsync(string userMessage);
}
```

**Benefits**:
- Swap between Bedrock, OpenAI GPT-4, Google Gemini, etc.
- Easy A/B testing of models
- Mock for unit testing
- Consistent interface across providers

**Example Alternative Implementation**:
```csharp
public class OpenAIChat : ILlmClient
{
    public async Task<string> GenerateResponseAsync(List<ChatMessage> conversationHistory)
    {
        // Call OpenAI API
    }
}
```

### AppSyncEventPublisher (`Services/AppSyncEventPublisher.cs`)

**Type**: Singleton service

Publishes real-time events to connected clients via AWS AppSync Events.

**Event Types**:
- `chat_created`
- `chat_status_changed`
- `message_received`
- `message_processing`
- `message_completed`
- `chat_closed`
- `error_occurred`

**Key Method**:
```csharp
public async Task PublishChatEventAsync(string chatId, string eventType, object data)
{
    var eventPayload = new
    {
        chatId,
        eventType,
        timestamp = DateTime.UtcNow,
        data
    };

    await _appSyncClient.PutEventsAsync(new PutEventsRequest
    {
        EventApiId = _configuration["AWS:AppSync:EventApiId"],
        Events = new List<Event>
        {
            new Event
            {
                EventType = $"chat/{chatId}/{eventType}",
                Data = JsonSerializer.Serialize(eventPayload)
            }
        }
    });
}
```

**Usage in Background Processing**:
```csharp
await _eventPublisher.PublishChatEventAsync(chatId, "message_received", userMessage);
// ... process with LLM ...
await _eventPublisher.PublishChatEventAsync(chatId, "message_completed", assistantMessage);
```

## Data Model

### Chat (DynamoDB Table: Chat)

```csharp
public class Chat : IItem
{
    public string Id { get; set; }              // Partition key
    public string ChatId { get; set; }          // Same as Id
    public string UserId { get; set; }          // User who owns this chat
    public ChatStatus Status { get; set; }      // active, closed, error
    public string Summary { get; set; }         // Brief conversation summary
    public string ChatMessagesId { get; set; }  // References ChatMessages table
    public int MessageCount { get; set; }       // Number of messages
    public long CreateUtcTick { get; set; }     // Creation timestamp
    public long UpdateUtcTick { get; set; }     // Last update timestamp
}
```

### ChatMessages (DynamoDB Table: ChatMessages)

```csharp
public class ChatMessages : IItem
{
    public string Id { get; set; }                      // Partition key (same as chatId)
    public string ChatMessagesId { get; set; }          // Same as Id
    public string ChatId { get; set; }                  // References Chat table
    public List<ChatMessage> Messages { get; set; }     // Array of messages
    public long CreateUtcTick { get; set; }
    public long UpdateUtcTick { get; set; }
}
```

### ChatMessage (Not persisted directly - part of ChatMessages.Messages array)

```csharp
public class ChatMessage
{
    public string MessageId { get; set; }       // Unique message ID
    public string Role { get; set; }            // "user" or "assistant"
    public string Content { get; set; }         // Message text
    public DateTime Timestamp { get; set; }     // When message was created
}
```

**Design Rationale**:
- Chat and ChatMessages are separate tables for optimization
- LIST /chat returns only Chat records (fast, small payload)
- Messages loaded separately when needed (GET /chat/{id}/messages)
- Reduces data transfer and improves list performance

## Dependency Injection

All services registered in `ServiceRepoExtensions.cs`:

```csharp
public static class ServiceRepoExtensions
{
    public static IServiceCollection AddChatSchemaRepo(this IServiceCollection services)
    {
        // Register repositories (scoped)
        services.TryAddScoped<IChatRepo, ChatRepo>();
        services.TryAddScoped<IChatMessagesRepo, ChatMessagesRepo>();

        // Register LLM client (singleton)
        services.TryAddSingleton<BedrockChat>();
        services.TryAddSingleton<ILlmClient>(sp => sp.GetRequiredService<BedrockChat>());

        // Register chat services (singleton)
        services.TryAddSingleton<AppSyncEventPublisher>();
        services.TryAddSingleton<ChatManagerService>();
        services.TryAddSingleton<IChatManagerService>(sp => sp.GetRequiredService<ChatManagerService>());

        // Register as IHostedService for background tasks
        services.AddHostedService(sp => sp.GetRequiredService<ChatManagerService>());

        // Register DynamoDB client
        services.TryAddAWSService<IAmazonDynamoDB>();
        services.TryAddAWSService<IAmazonBedrockRuntime>();

        return services;
    }
}
```

**Registration Notes**:
- **Repositories**: Scoped lifetime (one per request)
- **Services**: Singleton lifetime (one per application)
- **ChatManagerService**: Registered three times:
  1. As concrete type (for resolution)
  2. As IChatManagerService (for DI)
  3. As IHostedService (for StartAsync/StopAsync lifecycle)

## Flow Diagrams

### Create Chat Flow

```
Client
  │
  ├─ POST /chat (Chat object)
  │
  ▼
ChatModuleController
  │
  ├─ AddChatAsync(Chat)
  │
  ▼
ChatRepo.CreateAsync()
  │
  ├─ ChatManagerService.InitializeChatAsync()
  │   ├─ Generate chatId, chatMessagesId
  │   ├─ Create ConnectionChat (in-memory)
  │   ├─ Start background ProcessChatMessagesAsync() task
  │   ├─ If first chat: Start keep-alive task
  │   └─ Return initialized Chat
  │
  ├─ base.CreateAsync() → DynamoDB (persist Chat)
  │
  ├─ ChatMessagesRepo.CreateAsync() → DynamoDB (empty Messages array)
  │
  └─ Return Chat to client
```

### Send Message Flow

```
Client
  │
  ├─ POST /chat/{id}/messages (ChatMessage)
  │
  ▼
ChatModuleController
  │
  ├─ AddChatMessageAsync()
  │
  ▼
ChatMessagesRepo.AddMessageAsync()
  │
  ├─ ChatManagerService.ProcessUserMessageAsync()
  │   ├─ Set messageId, timestamp
  │   ├─ Add to in-memory chat.History
  │   ├─ Queue in chat.MessageQueue
  │   └─ Return message (immediately)
  │
  ├─ Load ChatMessages from DynamoDB
  ├─ Append message to Messages array
  ├─ Save to DynamoDB
  │
  └─ Return ChatMessage to client

Background Task (ProcessChatMessagesAsync):
  │
  ├─ Read from chat.MessageQueue
  ├─ Publish "message_received" event
  ├─ ILlmClient.GenerateResponseAsync(history)
  ├─ Create assistant ChatMessage
  ├─ Add to in-memory chat.History
  ├─ Update Chat.Summary
  └─ Publish "message_completed" event
```

### Retrieve Messages Flow

```
Client
  │
  ├─ GET /chat/{id}/messages?page=1&limit=50
  │
  ▼
ChatModuleController
  │
  ├─ GetChatMessagesAsync()
  │
  ▼
ChatMessagesRepo.GetMessagesAsync()
  │
  ├─ Try: ChatManagerService.GetChatHistoryAsync() (in-memory)
  │   └─ Success: Return paginated in-memory messages
  │
  └─ Catch InvalidOperationException:
      ├─ Load ChatMessages from DynamoDB
      ├─ Apply pagination
      └─ Return messages
```

## Testing Considerations

### Unit Testing

**Mock ILlmClient**:
```csharp
var mockLlm = new Mock<ILlmClient>();
mockLlm.Setup(x => x.GenerateResponseAsync(It.IsAny<List<ChatMessage>>()))
       .ReturnsAsync("Mocked response");
```

**Mock IChatManagerService**:
```csharp
var mockManager = new Mock<IChatManagerService>();
mockManager.Setup(x => x.InitializeChatAsync(It.IsAny<ICallerInfo>(), It.IsAny<Chat>()))
           .ReturnsAsync(new Chat { ChatId = "test-123", Status = ChatStatus.Active });
```

### Integration Testing

Test the full stack:
1. Create chat via API
2. Send message
3. Poll for assistant response
4. Verify DynamoDB persistence
5. Close chat
6. Verify cleanup

## Performance Characteristics

- **Chat Creation**: ~50-100ms (in-memory + DynamoDB write)
- **Message Queuing**: ~20-50ms (in-memory operation)
- **LLM Response**: ~2-5 seconds (depends on Bedrock)
- **Message Retrieval (active chat)**: ~10-20ms (in-memory)
- **Message Retrieval (inactive chat)**: ~50-100ms (DynamoDB read)
- **List Chats**: ~50-200ms (DynamoDB scan, depends on count)

## Error Handling

### Common Scenarios

**Chat Not Found**:
- Thrown by: `ChatManagerService.GetChatByIdAsync()`
- Caught by: `ChatRepo.ReadAsync()` → Falls back to DynamoDB
- Result: Auto-rehydration from persistent storage

**Unauthorized Access**:
- Thrown by: All ChatManagerService methods
- Message: "User {userId} does not own chat {chatId}"
- HTTP Status: 401 Unauthorized

**LLM Errors**:
- Caught in: `ProcessChatMessagesAsync()`
- Response: User-friendly error message
- Event: `error_occurred` published to client

## Monitoring and Observability

### Key Metrics to Track

- Active chats count (`_chats.Count`)
- Background tasks running
- Keep-alive semaphore state
- DynamoDB read/write throughput
- Bedrock API latency
- Message processing time

### Logging

Uses `ILogger<T>` throughout:
- `ChatManagerService`: Chat lifecycle events
- `ChatRepo`: CRUD operations
- `BedrockChat`: LLM API calls and errors
- `AppSyncEventPublisher`: Event publishing

## Best Practices

1. **Always use interfaces**: Depend on IChatManagerService, ILlmClient, not concrete types
2. **Verify ownership**: All operations check `callerInfo.LzUserId` matches chat.UserId
3. **Handle InvalidOperationException**: Indicates chat not in memory, fallback to DynamoDB
4. **Use CallerInfo**: All methods accept ICallerInfo for multi-tenancy support
5. **Let services handle IDs**: Don't pass pre-generated IDs, let services create them
6. **Cleanup resources**: Always close chats when done (prevents memory leaks)

## Future Enhancements

- [ ] **Redis Integration**: Shared in-memory state across multiple instances
- [ ] **Streaming Responses**: Real-time token delivery via AppSync
- [ ] **Multi-Model Support**: Additional ILlmClient implementations
- [ ] **Advanced Summarization**: Auto-generate Chat.Summary from conversations
- [ ] **Full-Text Search**: DynamoDB query optimization for message search
- [ ] **Rate Limiting**: Per-user quotas and throttling
- [ ] **Analytics**: Conversation metrics and user insights

## Related Documentation

- [ChatModule README](/Service/Modules/ChatModule/README.md) - Overall architecture
- [HOST-HEADER.md](/Service/Modules/ChatModule/HOST-HEADER.md) - Keep-alive host resolution
- [LazyMagic Repository Pattern](/docs/repository-pattern.md) - Framework patterns

## License

Copyright © 2025 MagicPets. All rights reserved.
