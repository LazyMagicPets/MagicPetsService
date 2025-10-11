# Chat Events - AWS AppSync Events Implementation

## Overview

This document describes the real-time chat events implementation using AWS AppSync Events API. The system provides bidirectional communication between the backend service and client applications for real-time chat updates, streaming responses, and status notifications.

## Architecture

### Components

```
┌─────────────────────┐
│   Client Apps       │
│  (MAUI/Blazor)      │
│                     │
│  WebSocket Client   │
└──────────┬──────────┘
           │
           │ wss:// (WebSocket)
           │
┌──────────▼──────────┐
│  AWS AppSync        │
│  Events API         │
│                     │
│  - WebSocket        │
│  - Pub/Sub          │
│  - Authentication   │
└──────────▲──────────┘
           │
           │ https:// (HTTP POST)
           │
┌──────────┴──────────┐
│  MagicPets Service  │
│                     │
│  ChatManagerService │
│  ↓                  │
│  AppSyncEvent       │
│  Publisher          │
└─────────────────────┘
```

### Flow

1. **Client Connection**: Client establishes WebSocket connection to AppSync Events API
2. **Subscription**: Client subscribes to chat channel: `/chat/{chatId}`
3. **Event Publishing**: Backend publishes events via HTTP POST to AppSync Events API
4. **Event Delivery**: AppSync Events API delivers events to subscribed clients via WebSocket
5. **Client Handling**: Client receives and processes events using ReactiveUI observables

## Event Types

All events follow a consistent structure:

```json
{
  "chatId": "uuid",
  "eventType": "EventType",
  "timestamp": "ISO8601",
  "data": { ... },
  "dataType": "TypeName"
}
```

### 1. Message_received

Fired when a user message is received by the backend.

**DataType**: `ChatMessage`

```json
{
  "chatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
  "eventType": "Message_received",
  "timestamp": "2025-10-11T17:34:17.123Z",
  "data": {
    "MessageId": "abc-123",
    "ChatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
    "Role": "User",
    "Content": "Hello, how are you?",
    "Timestamp": "2025-10-11T17:34:17.123Z",
    "Metadata": null
  },
  "dataType": "ChatMessage"
}
```

**Use Case**: Confirm message receipt, show "message sent" status

### 2. Message_processing

Fired when the LLM starts processing a message.

**DataType**: `Object`

```json
{
  "chatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
  "eventType": "Message_processing",
  "timestamp": "2025-10-11T17:34:17.329Z",
  "data": {
    "MessageId": "2cc2b41b-4ac5-48c1-b2b3-a701167ace16"
  },
  "dataType": "Object"
}
```

**Use Case**: Show loading indicator, initialize streaming message UI

### 3. Message_streaming

Fired for each chunk of text as the LLM generates the response. Multiple events are sent as the response streams.

**DataType**: `Object`

```json
{
  "chatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
  "eventType": "Message_streaming",
  "timestamp": "2025-10-11T17:34:18.120Z",
  "data": {
    "MessageId": "2cc2b41b-4ac5-48c1-b2b3-a701167ace16",
    "Chunk": "! It's nice"
  },
  "dataType": "Object"
}
```

**Use Case**: Append chunk to message UI, show typing animation

**Important**: Each `Chunk` contains only the NEW text to append. The client accumulates chunks:

```csharp
// Client-side pseudocode
var streamingMessages = new Dictionary<string, StringBuilder>();

void OnMessageStreaming(event) {
    if (!streamingMessages.ContainsKey(event.MessageId)) {
        streamingMessages[event.MessageId] = new StringBuilder();
    }
    streamingMessages[event.MessageId].Append(event.Chunk);
    UpdateUI(event.MessageId, streamingMessages[event.MessageId].ToString());
}
```

### 4. Message_completed

Fired when the LLM completes generating the full response.

**DataType**: `ChatMessage`

```json
{
  "chatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
  "eventType": "Message_completed",
  "timestamp": "2025-10-11T17:34:20.456Z",
  "data": {
    "MessageId": "2cc2b41b-4ac5-48c1-b2b3-a701167ace16",
    "ChatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
    "Role": "Assistant",
    "Content": "Hello! It's nice to have a conversation with you. How can I help you today?",
    "Timestamp": "2025-10-11T17:34:17.329Z",
    "Metadata": null
  },
  "dataType": "ChatMessage"
}
```

**Use Case**: Finalize message display, hide loading indicators, persist to local state

### 5. Chat_status_changed

Fired when the chat status changes (Active, Processing, Error, Closed).

**DataType**: `Object`

```json
{
  "chatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
  "eventType": "Chat_status_changed",
  "timestamp": "2025-10-11T17:34:20.789Z",
  "data": {
    "Status": "Active"
  },
  "dataType": "Object"
}
```

**Use Case**: Update chat status in UI, enable/disable input controls

### 6. Error_occurred

Fired when an error occurs during message processing.

**DataType**: `Object`

```json
{
  "chatId": "f412d22a-b580-4c80-b0c6-9dec2a6ce102",
  "eventType": "Error_occurred",
  "timestamp": "2025-10-11T17:34:21.000Z",
  "data": {
    "Error": "Failed to connect to LLM service"
  },
  "dataType": "Object"
}
```

**Use Case**: Display error message, allow retry

## Backend Implementation

### Publishing Events

Events are published using the `IAppSyncEventPublisher` service:

```csharp
// Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs

public interface IAppSyncEventPublisher
{
    Task PublishChatEventAsync(string chatId, ChatEvent sessionEvent);
    Task PublishMessageEventAsync(string chatId, ChatMessage message);
    Task PublishChatStatusEventAsync(string chatId, ChatStatus status);
    Task PublishErrorEventAsync(string chatId, string error);
}
```

**Example - Publishing a message received event:**

```csharp
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Message_received,
    ChatId = chat.ChatId,
    Timestamp = DateTime.UtcNow,
    Data = message
});
```

### Configuration

The service requires configuration for the AppSync Events API endpoints:

```json
{
  "AWS": {
    "Region": "us-west-2",
    "AppSync": {
      "TenantEventsApi": {
        "HttpDomain": "24njcduygfd35m34apqhaaqs6e.appsync-api.us-west-2.amazonaws.com",
        "ApiKey": "da2-xxx..."
      },
      "ConsumerEventsApi": {
        "HttpDomain": "kwzvarrv3na35ebufl3hqqpmdq.appsync-api.us-west-2.amazonaws.com",
        "ApiKey": "da2-yyy..."
      }
    }
  }
}
```

**Authentication Methods:**

1. **API Key** (Currently Used): Simple, passed via `x-api-key` header
2. **IAM/SigV4**: AWS signature-based authentication (available but not currently used)

Toggle via `UseApiKeyAuth` constant in `AppSyncEventPublisher.cs` (line 13).

### Event Publishing Flow

1. **ChatManagerService** creates a `ChatEvent` with type, timestamp, and data
2. **AppSyncEventPublisher** extracts the data type name before serialization
3. Event payload is created with `chatId`, `eventType`, `timestamp`, `data`, and `dataType`
4. Payload is serialized to JSON string (AppSync Events requires JSON strings in events array)
5. HTTP POST to `https://{domain}/event` with channel path `/chat/{chatId}`
6. AppSync Events API delivers to all subscribed clients on that channel

## Client Implementation

### WebSocket Connection

Clients use `AppSyncEventsWebSocketClient` to connect and subscribe:

```csharp
// BaseAppLib/BaseApp.ViewModels/Services/AppSyncEventsWebSocketClient.cs

public interface IAppSyncEventsWebSocketClient : IDisposable
{
    Task<bool> ConnectAsync(string eventApiUrl, CancellationToken cancellationToken = default);
    Task<string> SubscribeAsync(string channelPath, CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(string subscriptionId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    IObservable<ChatEventReceivedEventArgs> EventReceived { get; }
    bool IsConnected { get; }
}
```

### Connection Process

1. **URL Transformation**: HTTP domain → WebSocket domain
   - Input: `https://xxx.appsync-api.region.amazonaws.com`
   - Output: `wss://xxx.appsync-realtime-api.region.amazonaws.com/event/realtime`
   - Note: `appsync-api` → `appsync-realtime-api` subdomain change

2. **Authentication**: WebSocket subprotocols
   ```csharp
   // Primary subprotocol
   webSocket.Options.AddSubProtocol("aws-appsync-event-ws");

   // Auth subprotocol with Base64URL-encoded JSON
   var authHeader = new { Authorization = token, host = httpHost };
   var authJson = JsonSerializer.Serialize(authHeader);
   var authBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authJson))
       .TrimEnd('=')
       .Replace('+', '-')
       .Replace('/', '_');
   webSocket.Options.AddSubProtocol($"header-{authBase64}");
   ```

3. **Connection Init**: Send `connection_init` message after WebSocket connects

4. **Subscription**: Subscribe to chat channel
   ```csharp
   await SubscribeAsync("/chat/{chatId}");
   ```

### Event Handling with ReactiveUI

Events are exposed as `IObservable<ChatEventReceivedEventArgs>`:

```csharp
public class ChatEventReceivedEventArgs : EventArgs
{
    public string SubscriptionId { get; set; }
    public string ChatId { get; set; }
    public string EventType { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public object? Data { get; set; }
    public string? DataType { get; set; }
}
```

**ChatViewModel subscribes to events for its specific chat:**

```csharp
_eventSubscription = _chatEventsService.ChatEvents
    .Where(e => e.ChatId == Id)  // Filter for this chat only
    .ObserveOn(RxApp.MainThreadScheduler)  // Marshal to UI thread
    .Subscribe(async eventArgs =>
    {
        await HandleChatEventAsync(eventArgs);
    });
```

**Handle events based on type:**

```csharp
private async Task HandleChatEventAsync(ChatEventReceivedEventArgs eventArgs)
{
    switch (eventArgs.EventType)
    {
        case "Message_received":
        case "Message_completed":
            await MessagesViewModel.ReadAsync(forceload: true);
            break;

        case "Message_streaming":
            // Append chunk to streaming message UI
            HandleStreamingChunk(eventArgs);
            break;

        case "Message_processing":
            // Show loading indicator
            ShowProcessingIndicator(eventArgs);
            break;

        case "Error_occurred":
            // Display error
            ShowError(eventArgs);
            break;
    }
}
```

### Initialization Flow

1. **ChatsViewModel** initializes the `ChatEventsService` once (fire-and-forget):
   ```csharp
   _ = InitializeChatEventsAsync();
   ```

2. **ChatEventsService.InitializeAsync()** connects to WebSocket:
   ```csharp
   var eventApiUrl = _clientConfig.GetCurrentEventsApiUrl();
   var connected = await _webSocketClient.ConnectAsync(eventApiUrl);
   ```

3. **ChatViewModel.InitializeEventsAsync()** subscribes to its channel:
   ```csharp
   _subscriptionId = await _chatEventsService.SubscribeToChatAsync(Id);
   ```

4. Events flow: AppSync → WebSocket → ChatEventsService → ChatViewModel

## Configuration

### Server-Side (CloudFormation/SAM)

The AppSync Events API is configured in AWS CloudFormation templates:

```yaml
# Service/AWSTemplates/Snippets/sam.service.appsync-events.yaml

Resources:
  TenantEventsApi:
    Type: AWS::AppSync::Api
    Properties:
      Name: !Sub "${AWS::StackName}-TenantEventsApi"
      EventConfig:
        AuthProviders:
          - AuthType: API_KEY
        ConnectionAuthModes:
          - AuthType: API_KEY
        DefaultPublishAuthModes:
          - AuthType: API_KEY
        DefaultSubscribeAuthModes:
          - AuthType: API_KEY

  TenantEventsApiKey:
    Type: AWS::AppSync::ApiKey
    Properties:
      ApiId: !GetAtt TenantEventsApi.ApiId
```

**Exported to Lambda environment:**

```yaml
Environment:
  Variables:
    AWS_REGION: !Ref AWS::Region
    AWS__AppSync__TenantEventsApi__HttpDomain: !GetAtt TenantEventsApi.Dns.Http
    AWS__AppSync__TenantEventsApi__ApiKey: !GetAtt TenantEventsApiKey.ApiKey
```

### Client-Side (config file)

The client receives the Events API configuration from the backend:

```json
{
  "meta": {
    "wsUrl": "wss://...",
    "tenantKey": "mp",
    "awsRegion": "us-west-2"
  },
  "authConfigs": {
    "TenantAuth": { "ClientId": "...", "MetadataUrl": "..." },
    "ConsumerAuth": { "ClientId": "...", "MetadataUrl": "..." }
  },
  "eventsApis": {
    "TenantAuth": {
      "authConfig": "TenantAuth",
      "wsUrl": "https://24njcduygfd35m34apqhaaqs6e.appsync-api.us-west-2.amazonaws.com"
    },
    "ConsumerAuth": {
      "authConfig": "ConsumerAuth",
      "wsUrl": "https://kwzvarrv3na35ebufl3hqqpmdq.appsync-api.us-west-2.amazonaws.com"
    }
  }
}
```

**Client code retrieves the URL:**

```csharp
// LazyMagic/LazyMagic.Client.Base/ClientConfig/OidcConfig.cs
public string? GetCurrentEventsApiUrl()
{
    foreach (var eventsApi in EventsApis.Values)
    {
        var authConfig = eventsApi["authConfig"]?.ToString();
        if (string.Equals(authConfig, SelectedAuthConfig, StringComparison.OrdinalIgnoreCase))
        {
            return eventsApi["wsUrl"]?.ToString();
        }
    }
    return null;
}
```

## WebSocket Protocol

### Connection Messages

**1. connection_init** (Client → Server)
```json
{"type": "connection_init"}
```

**2. connection_ack** (Server → Client)
```json
{"type": "connection_ack", "connectionTimeoutMs": 300000}
```

**3. ka** (Server → Client, periodic keep-alive)
```json
{"type": "ka"}
```

### Subscription Messages

**1. subscribe** (Client → Server)
```json
{
  "type": "subscribe",
  "id": "unique-subscription-id",
  "channel": "/chat/{chatId}",
  "authorization": {
    "Authorization": "Bearer eyJ...",
    "host": "xxx.appsync-api.region.amazonaws.com"
  }
}
```

**2. subscribe_success** (Server → Client)
```json
{"id": "subscription-id", "type": "subscribe_success"}
```

**3. subscribe_error** (Server → Client)
```json
{
  "id": "subscription-id",
  "type": "subscribe_error",
  "errors": [{"message": "Error details"}]
}
```

**4. unsubscribe** (Client → Server)
```json
{"type": "unsubscribe", "id": "subscription-id"}
```

### Data Messages

**data** (Server → Client)
```json
{
  "id": "subscription-id",
  "type": "data",
  "event": "{\"chatId\":\"...\",\"eventType\":\"Message_received\",\"timestamp\":\"...\",\"data\":{...},\"dataType\":\"ChatMessage\"}"
}
```

**Note**: The `event` field is a JSON STRING, not an object. The client must parse it:

```csharp
if (eventElement.ValueKind == JsonValueKind.String)
{
    var eventJson = eventElement.GetString();
    using var eventDoc = JsonDocument.Parse(eventJson);
    eventData = eventDoc.RootElement.Clone();
}
```

### Error Messages

**error** (Server → Client)
```json
{
  "type": "error",
  "errors": [
    {
      "errorType": "UnsupportedOperation",
      "message": "Operation not supported through the realtime channel"
    }
  ]
}
```

## Channel Namespace Pattern

Channels follow a hierarchical pattern with 1-5 segments:

- `/chat/{chatId}` - Messages for specific chat
- `/user/{userId}` - User-specific notifications (future)
- `/tenant/{tenantId}` - Tenant-wide broadcasts (future)

Each segment can be up to 50 characters. Total channel path is used for pub/sub routing.

## Security

### Authentication

**Server-Side (Publishing):**
- API Key passed via `x-api-key` HTTP header
- Alternative: IAM/SigV4 signing (configured but not currently used)

**Client-Side (WebSocket):**
- JWT Bearer token from OIDC authentication
- Passed via WebSocket subprotocol as Base64URL-encoded JSON
- Format: `header-{base64url({"Authorization":"Bearer token","host":"domain"})}`

### Authorization

Events are published to channels based on `chatId`. Clients must:
1. Be authenticated (valid JWT token)
2. Subscribe to specific chat channels they own
3. Backend verifies ownership before allowing chat operations

**Future Enhancement**: Add server-side authorization rules in AppSync Events API to enforce chat ownership at the API level.

## Error Handling

### Connection Errors

**404 - Not Found**: Incorrect URL or endpoint not configured
- Verify: URL format, subdomain transformation, path suffix

**403 - Forbidden**: Authentication failure
- Verify: Token validity, token format, host parameter

**400 - Bad Request**: Invalid subprotocol or message format
- Verify: Subprotocol format, message JSON structure

### Client-Side Retry Logic

```csharp
private async Task<bool> ConnectWithRetryAsync(int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            var connected = await _webSocketClient.ConnectAsync(eventApiUrl);
            if (connected) return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection attempt {Attempt} failed", i + 1);
            if (i < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i))); // Exponential backoff
            }
        }
    }
    return false;
}
```

## Performance Considerations

### Bandwidth Optimization

1. **Streaming Events**: Only send new chunks, not accumulated content
   - Before: ~1KB per chunk × 50 chunks = 50KB
   - After: ~20 bytes per chunk × 50 chunks = 1KB

2. **Event Filtering**: Clients filter events by `chatId` on client side
   - Consider: Server-side filtering via AppSync resolver (future)

3. **Connection Pooling**: Reuse WebSocket connection across multiple chats
   - Current: Single connection, multiple channel subscriptions
   - Good for: ≤100 active chats per user

### Scalability

**AppSync Events API Limits:**
- Connections: 100,000 per API
- Message rate: 1,000 messages/second per connection
- Message size: 128KB per message
- Subscriptions: 100 per connection

**MagicPets Usage:**
- Typical: 1-5 active chats per user
- Peak: ~100 streaming chunks per chat response
- Average message size: 500 bytes
- Well within limits for expected usage

## Monitoring and Observability

### Server-Side Logging

```csharp
_logger.LogInformation(
    "Publishing session event: {EventType} for session: {SessionId} with data type: {DataType}",
    sessionEvent.EventType, chatId, dataTypeName ?? "null");
```

**Key Metrics to Monitor:**
- Event publish success rate
- Event publish latency
- Event size distribution
- Error rate by type

### Client-Side Logging

```csharp
_logger.LogInformation(
    "Received chat event: {EventType} (DataType: {DataType}) for chat: {ChatId}",
    eventType, dataType ?? "null", chatId);
```

**Key Metrics to Monitor:**
- Connection success rate
- Reconnection frequency
- Event delivery latency
- Event processing errors

## Troubleshooting

### No Events Received

1. **Check WebSocket connection**: Look for `connection_ack` message
2. **Verify subscription**: Look for `subscribe_success` message
3. **Check channel path**: Ensure `/chat/{chatId}` matches exactly
4. **Verify auth config**: Ensure client uses same auth as server
5. **Check backend logs**: Confirm events are being published

### Events Not Reaching Client

1. **Verify subscription ID**: Matches between subscribe and data messages
2. **Check event filtering**: Client-side `Where(e => e.ChatId == Id)` logic
3. **Inspect raw WebSocket messages**: Enable debug logging
4. **Check JSON parsing**: Event field is a string, not object

### Authentication Failures

1. **Token expiration**: Tokens refresh automatically via `IOIDCService`
2. **Host mismatch**: Ensure `host` in auth matches HTTP domain (not WebSocket domain)
3. **Base64URL encoding**: Check padding removal and character substitution
4. **Subprotocol order**: `aws-appsync-event-ws` must be first

## Future Enhancements

### Planned Features

1. **Message Read Receipts**: Track when messages are viewed
2. **Typing Indicators**: Show when user is typing
3. **Presence**: Show online/offline status
4. **Message Reactions**: Like, emoji reactions
5. **File Upload Progress**: Real-time upload status
6. **Multi-Device Sync**: Sync across user's devices

### Optimization Opportunities

1. **Message Batching**: Combine multiple streaming chunks
2. **Compression**: Gzip event payloads
3. **Delta Updates**: Send only changed fields for updates
4. **Connection Pooling**: Share connection across app instances

### Infrastructure Improvements

1. **CloudWatch Metrics**: Custom metrics for event throughput
2. **X-Ray Tracing**: Trace event flow end-to-end
3. **Alarms**: Alert on high error rates or latency
4. **Cost Optimization**: Monitor AppSync Events API costs

## References

### Documentation

- [AWS AppSync Events API Documentation](https://docs.aws.amazon.com/appsync/latest/eventapi/)
- [WebSocket Protocol](https://docs.aws.amazon.com/appsync/latest/eventapi/event-api-websocket-protocol.html)
- [ReactiveUI Documentation](https://www.reactiveui.net/)

### Code Locations

**Backend:**
- Events Publisher: `Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs`
- Chat Manager: `Service/Schemas/ChatSchemaRepo/Services/ChatManagerService.cs`
- Event Interfaces: `Service/Schemas/ChatSchemaRepo/Services/IAppSyncEventPublisher.cs`
- Event DTOs: `Service/Schemas/ChatSchema/DTOs/ChatEvent.g.cs`
- Event Types: `Service/Schemas/ChatSchema/DTOs/ChatEventType.g.cs`

**Client:**
- WebSocket Client: `BaseAppLib/BaseApp.ViewModels/Services/AppSyncEventsWebSocketClient.cs`
- Events Service: `BaseAppLib/BaseApp.ViewModels/Services/ChatEventsService.cs`
- Chat ViewModel: `BaseAppLib/BaseApp.ViewModels/Session/Chat/ChatViewModel.cs`
- Chats ViewModel: `BaseAppLib/BaseApp.ViewModels/Session/Chat/ChatsViewModel.cs`
- Config Extensions: `BaseAppLib/BaseApp.ViewModels/Config/LzClientConfigExtensions.cs`

**Configuration:**
- OidcConfig: `LazyMagic/LazyMagic.Client.Base/ClientConfig/OidcConfig.cs`
- ClientConfig: `LazyMagic/LazyMagic.Client.Base/ClientConfig/LzClientConfig.cs`

## Version History

- **v1.0** (2025-10-11): Initial implementation
  - WebSocket connection and subscription
  - All 6 event types implemented
  - ReactiveUI integration
  - Streaming chunk optimization
  - DataType field added

---

**Last Updated**: 2025-10-11
**Authors**: LazyMagic Team, Claude (Anthropic AI Assistant)
