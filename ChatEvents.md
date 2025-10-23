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
│  IChatEvent         │
│  Publisher          │
│  (Domain Layer)     │
│  ↓                  │
│  IWsEvent           │
│  Publisher          │
│  (Transport Layer)  │
│  ↓                  │
│  AppSyncWs          │
│  EventPublisher     │
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

### API Endpoints

The chat system exposes RESTful API endpoints through the ChatModule:

#### Chat Management

- **POST /chat** - Create new chat session
  - Routes to: `ChatManagerService.CreateChatAsync(callerInfo, body)`
  - Creates Chat entity in DynamoDB
  - Creates ChatContext entity in DynamoDB
  - Initializes in-memory ConnectionChat with background processing
  - Returns: `Chat` object with generated `ChatId`

- **GET /chat** - List all chats for authenticated user
  - Routes to: `ChatManagerService.ListChatsAsync(callerInfo)`
  - Returns chats from DynamoDB, enhanced with in-memory state if active

- **GET /chat/{chatId}** - Get specific chat
  - Routes to: `ChatManagerService.GetChatAsync(callerInfo, chatId)`
  - Returns in-memory instance if active, otherwise loads from DynamoDB

- **PUT /chat** - Update chat metadata
  - Routes to: `ChatManagerService.UpdateChatAsync(callerInfo, body)`
  - Updates both in-memory and persistent storage

- **DELETE /chat/{chatId}** - Delete chat
  - Routes to: `ChatManagerService.DeleteChatAsync(callerInfo, chatId)`
  - Stops background processing, persists messages, deletes from DynamoDB

#### Message Management

- **POST /chat/{chatId}/messages** - Send message to chat
  - Routes to: `ChatManagerService.SendMessageAsync(callerInfo, chatId, body)`
  - **Flow**:
    1. Ensures chat exists in memory (resumes from DynamoDB if needed)
    2. Validates chat is active and user owns it
    3. Sets `MessageId`, `ChatId`, `Timestamp`, `Role=User`
    4. Publishes `Message_received` event via AppSync Events
    5. Enqueues message to `Channel<ChatMessage>` for background processing
    6. Returns immediately with `ChatMessage` object (non-blocking)
    7. Background task processes LLM request and publishes streaming events
  - **Important**: Returns immediately - LLM response comes via AppSync Events

- **GET /chat/{chatId}/messages** - Get message history
  - Routes to: `ChatManagerService.GetMessagesAsync(callerInfo, chatId, page, limit)`
  - Returns messages from in-memory history if active, otherwise from DynamoDB
  - Supports pagination via `page` and `limit` query parameters

### Orchestrator Pattern

**ChatManagerService** acts as the orchestrator, coordinating between:
- **Data Layer**: `IChatRepo`, `IChatContextRepo` (DynamoDB persistence)
- **In-Memory State**: `ConnectionChat` instances with message queues
- **Background Processing**: LLM orchestration via `ILlmClient`
- **Event Publishing**: Real-time updates via `IChatEventPublisher`

**Key Responsibilities**:
1. Manages dual-state (in-memory + DynamoDB) for active chats
2. Routes API calls to appropriate persistence or in-memory operations
3. Ensures chat ownership and authorization
4. Handles chat lifecycle (create, resume, delete)
5. Coordinates message processing and event publishing

### Message Processing Flow

```
Client → POST /chat/{chatId}/messages
   ↓
ChatManagerService.SendMessageAsync()
   ├─→ Validate chat exists & user owns it
   ├─→ Set message metadata (ID, timestamp, role)
   ├─→ Publish Message_received event ────────→ AppSync Events → Client
   ├─→ Enqueue to Channel<ChatMessage>
   └─→ Return ChatMessage (200 OK) ──────────→ Client

Background Task (running continuously)
   ↓
Read from Channel<ChatMessage>
   ├─→ Publish Message_processing event ──────→ AppSync Events → Client
   ├─→ Call ILlmClient.GenerateResponseAsync()
   │    └─→ Bedrock Claude 3 Sonnet (streaming)
   ├─→ For each chunk:
   │    └─→ Publish Message_streaming event ──→ AppSync Events → Client
   ├─→ Publish Message_completed event ───────→ AppSync Events → Client
   ├─→ Persist messages to DynamoDB (batch)
   └─→ Publish Chat_status_changed event ─────→ AppSync Events → Client
```

### Two-Layer Architecture for Event Publishing

The backend uses a two-layer architecture for event publishing:

#### Domain Layer: `IChatEventPublisher`

Provides high-level, business-focused methods for publishing chat events.

**All methods accept `ICallerInfo` to enable multi-tenant AppSync Events routing:**

```csharp
// Service/Schemas/ChatSchemaRepo/Services/IChatEventPublisher.cs

public interface IChatEventPublisher
{
    Task PublishUserMessageAsync(string chatId, ChatMessage message, ICallerInfo callerInfo);
    Task PublishProcessingStartedAsync(string chatId, string messageId, ICallerInfo callerInfo);
    Task PublishStreamingChunkAsync(string chatId, string messageId, string chunk, ICallerInfo callerInfo);
    Task PublishMessageCompletedAsync(string chatId, ChatMessage message, ICallerInfo callerInfo);
    Task PublishErrorAsync(string chatId, string error, ICallerInfo callerInfo);
    Task PublishStatusChangedAsync(string chatId, ChatStatus status, ICallerInfo callerInfo);
}
```

**Key Enhancement:** The `ICallerInfo` parameter contains `Authname` (e.g., "tenantauth", "consumerauth") which the transport layer uses to route events to the correct AppSync Events API.

#### Transport Layer: `IWsEventPublisher`

Provides platform-agnostic WebSocket event publishing with multi-tenant support:

```csharp
// Service/Schemas/ChatSchemaRepo/Services/IWsEventPublisher.cs

public interface IWsEventPublisher
{
    /// <param name="callerInfo">Caller authentication context for EventsApi selection</param>
    Task PublishAsync<T>(
        string channel,
        string eventType,
        T data,
        Dictionary<string, object>? metadata = null,
        ICallerInfo? callerInfo = null);
}
```

**Key Enhancement:** The optional `callerInfo` parameter enables the `AppSyncWsEventPublisher` implementation to dynamically select the correct EventsApi based on `callerInfo.Authname`.

### Publishing Events

Business logic uses the domain layer interface for simplified event publishing.

**Example - Publishing a message received event:**

```csharp
// BEFORE (6 lines, no multi-tenant support):
await _eventPublisher.PublishChatEventAsync(chat.ChatId, new ChatEvent
{
    EventType = ChatEventType.Message_received,
    ChatId = chat.ChatId,
    Timestamp = DateTime.UtcNow,
    Data = message
});

// AFTER (1 line, with multi-tenant routing):
await _eventPublisher.PublishUserMessageAsync(chatId, message, callerInfo);
```

**All Domain Layer Methods with `ICallerInfo`:**

```csharp
// User message received
await _eventPublisher.PublishUserMessageAsync(chatId, message, callerInfo);

// Processing started
await _eventPublisher.PublishProcessingStartedAsync(chatId, messageId, callerInfo);

// Streaming chunk
await _eventPublisher.PublishStreamingChunkAsync(chatId, messageId, chunk, callerInfo);

// Message completed
await _eventPublisher.PublishMessageCompletedAsync(chatId, assistantMessage, callerInfo);

// Error occurred
await _eventPublisher.PublishErrorAsync(chatId, "Error message", callerInfo);

// Status changed
await _eventPublisher.PublishStatusChangedAsync(chatId, ChatStatus.Active, callerInfo);
```

**Why `ICallerInfo` is Required:**
- ChatManagerService receives `callerInfo` from API controller (extracted from JWT)
- `callerInfo.Authname` determines which AppSync Events API to use
- Enables single container to route tenant and consumer events to separate APIs
- Example: Tenant request (`Authname="tenantauth"`) → `tenantauthEventsApi`

### Implementation Details

**ChatEventPublisher** (Domain Implementation):
```csharp
public class ChatEventPublisher : IChatEventPublisher
{
    private readonly IWsEventPublisher _wsPublisher;

    public async Task PublishUserMessageAsync(string chatId, ChatMessage message)
    {
        await _wsPublisher.PublishAsync(
            channel: $"/chat/{chatId}",
            eventType: ChatEventType.Message_received.ToString(),
            data: message,
            metadata: new Dictionary<string, object>
            {
                { "dataType", nameof(ChatMessage) }
            });
    }
    // ... other methods
}
```

**AppSyncWsEventPublisher** (AWS AppSync Transport Implementation):
```csharp
public class AppSyncWsEventPublisher : IWsEventPublisher
{
    public async Task PublishAsync<T>(
        string channel,
        string eventType,
        T data,
        Dictionary<string, object>? metadata = null)
    {
        // Build AppSync-specific event payload
        var eventPayload = new
        {
            chatId = ExtractChatIdFromChannel(channel),
            eventType = eventType,
            timestamp = DateTime.UtcNow.ToString("O"),
            data = data,
            dataType = metadata?.GetValueOrDefault("dataType")?.ToString()
                ?? typeof(T).Name
        };

        // Publish via AppSync HTTP API
        await PublishEventAsync(channel, eventPayload, eventType);
    }
}
```

### Benefits of Two-Layer Architecture

1. **Simplified Business Logic**: 83% less code in ChatManagerService (6 lines → 1 line per event)
2. **Platform Independence**: Easy to add SignalR, Azure Event Grid, or other transports
3. **Clear Separation**: Domain logic isolated from transport details
4. **Better Testing**: Mock at either layer depending on test needs
5. **Type Safety**: Strongly-typed domain methods prevent mistakes

### Configuration

The service supports **multiple AppSync Events APIs** with dynamic routing based on caller authentication.

#### Multi-Tenant AppSync Events Architecture

The system uses **`ICallerInfo.Authname`** to dynamically route events to the correct AppSync Events API:

- **Tenant/Store authenticated requests** (`Authname = "tenantauth"`) → `tenantauthEventsApi`
- **Consumer authenticated requests** (`Authname = "consumerauth"`) → `consumerauthEventsApi`

This allows a single AppRunner container to serve both tenant and consumer users, with events published to their respective AppSync APIs.

#### Configuration Structure

```json
{
  "AWS": {
    "Region": "us-west-2",
    "AppSync": {
      "tenantauthEventsApi": {
        "HttpDomain": "24njcduygfd35m34apqhaaqs6e.appsync-api.us-west-2.amazonaws.com",
        "ApiKey": "da2-xxx...",
        "Region": "us-west-2"
      },
      "consumerauthEventsApi": {
        "HttpDomain": "kwzvarrv3na35ebufl3hqqpmdq.appsync-api.us-west-2.amazonaws.com",
        "ApiKey": "da2-yyy...",
        "Region": "us-west-2"
      }
    }
  }
}
```

#### Dynamic EventsApi Resolution

The `AppSyncWsEventPublisher` resolves the correct EventsApi configuration using a **convention-based approach**:

```csharp
// AppSyncWsEventPublisher.cs - ResolveEventsApiConfig()

private (string? httpDomain, string? apiKey) ResolveEventsApiConfig(string? authName)
{
    if (string.IsNullOrEmpty(authName))
    {
        _logger.LogWarning("No authName provided in CallerInfo. Cannot resolve EventsApi configuration.");
        return (null, null);
    }

    // Convention-based mapping: {authname}EventsApi
    // Example: authName="tenantauth" → "AWS:AppSync:tenantauthEventsApi"
    var configKey = $"AWS:AppSync:{authName}EventsApi";
    var httpDomain = _configuration[$"{configKey}:HttpDomain"];
    var apiKey = _configuration[$"{configKey}:ApiKey"];

    return (httpDomain, apiKey);
}
```

**Resolution Flow:**
1. API request arrives with JWT token (contains auth context)
2. `ICallerInfo` extracts `Authname` from authentication middleware (e.g., "tenantauth")
3. `ChatManagerService` passes `callerInfo` to all event publisher methods
4. `ChatEventPublisher` forwards `callerInfo` to `IWsEventPublisher.PublishAsync()`
5. `AppSyncWsEventPublisher` calls `ResolveEventsApiConfig(callerInfo.Authname)`
6. Configuration key constructed: `AWS:AppSync:{authname}EventsApi`
7. Event published to the correct AppSync Events API

**Benefits:**
- **Single Container, Multiple APIs**: One AppRunner serves both tenant and consumer users
- **Automatic Routing**: No manual API selection required
- **Convention-Based**: Add new auth contexts by following naming pattern
- **Fail-Safe**: Logs warnings if EventsApi configuration is missing

**Example - AppRunner receives from CloudFormation:**

AppRunner containers receive environment variables for **both** AppSync Events APIs:

```yaml
RuntimeEnvironmentVariables:
  # Tenant/Store Events API
  - Name: AWS__AppSync__tenantauthEventsApi__HttpDomain
    Value: !GetAtt tenantauthEventsApi.Dns.Http
  - Name: AWS__AppSync__tenantauthEventsApi__ApiKey
    Value: !GetAtt tenantauthEventsApiApiKey.ApiKey
  - Name: AWS__AppSync__tenantauthEventsApi__Region
    Value: !Ref AWS::Region

  # Consumer Events API
  - Name: AWS__AppSync__consumerauthEventsApi__HttpDomain
    Value: !GetAtt consumerauthEventsApi.Dns.Http
  - Name: AWS__AppSync__consumerauthEventsApi__ApiKey
    Value: !GetAtt consumerauthEventsApiApiKey.ApiKey
  - Name: AWS__AppSync__consumerauthEventsApi__Region
    Value: !Ref AWS::Region
```

**Example - LocalWebService reads from CloudFormation stack:**
```csharp
// Startup.g.cs reads stack outputs and sets auth-specific config
_configuration["AWS:AppSync:tenantauthEventsApi:HttpDomain"] = tenantEventsStackOutput.HttpDomain;
_configuration["AWS:AppSync:tenantauthEventsApi:ApiKey"] = tenantEventsStackOutput.ApiKey;

_configuration["AWS:AppSync:consumerauthEventsApi:HttpDomain"] = consumerEventsStackOutput.HttpDomain;
_configuration["AWS:AppSync:consumerauthEventsApi:ApiKey"] = consumerEventsStackOutput.ApiKey;
```

**Auto-Deployment Behavior:**

AppRunner auto-deployment occurs when:
1. **New ECR Image Push**: `AutoDeploymentsEnabled: true` triggers automatic deployment when a new image with the matching tag is pushed to ECR
2. **CloudFormation Stack Update**: Changes to `AWS::AppRunner::Service` resource properties (including `RuntimeEnvironmentVariables`) automatically trigger a service update and redeployment
   - CloudFormation detects property changes
   - Pulls the latest image from ECR
   - Launches new instances with updated configuration
   - Performs rolling deployment to replace old instances
   - **No manual AppRunner restart required**

**Important**: When updating environment variables in the CloudFormation template, the service will automatically restart with the new configuration during stack update. Active sessions will be lost during the deployment (see AppRunner.md for session lifecycle details).

**Authentication Methods:**

1. **API Key** (Currently Used): Simple, passed via `x-api-key` header
2. **IAM/SigV4**: AWS signature-based authentication (available but not currently used)

Toggle via `UseApiKeyAuth` constant in `AppSyncWsEventPublisher.cs`.

### Event Publishing Flow

1. **ChatManagerService** calls domain method with `callerInfo`:
   ```csharp
   await _eventPublisher.PublishUserMessageAsync(chatId, message, callerInfo);
   ```

2. **ChatEventPublisher** (domain layer):
   - Constructs channel path: `/chat/{chatId}`
   - Converts event type enum to string: `ChatEventType.Message_received.ToString()`
   - Adds metadata with dataType: `nameof(ChatMessage)`
   - **Passes `callerInfo` to transport layer**:
   ```csharp
   await _wsPublisher.PublishAsync(channel, eventType, data, metadata, callerInfo);
   ```

3. **AppSyncWsEventPublisher** (transport layer):
   - **Resolves correct EventsApi using `callerInfo.Authname`**:
     ```csharp
     var (httpDomain, apiKey) = ResolveEventsApiConfig(callerInfo?.Authname);
     // Example: "tenantauth" → AWS:AppSync:tenantauthEventsApi
     ```
   - Extracts chatId from channel path
   - Builds AppSync event payload with `chatId`, `eventType`, `timestamp`, `data`, `dataType`
   - Serializes payload to JSON string (AppSync Events requires JSON strings in events array)
   - HTTP POST to `https://{httpDomain}/event` with API Key or IAM/SigV4 authentication

4. **AppSync Events API** (auth-specific) delivers event to all subscribed clients on that channel:
   - **tenantauthEventsApi** → Tenant/Store clients subscribed to `/chat/{chatId}`
   - **consumerauthEventsApi** → Consumer clients subscribed to `/chat/{chatId}`

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
- Domain Interface: `Service/Schemas/ChatSchemaRepo/Services/IChatEventPublisher.cs`
- Domain Implementation: `Service/Schemas/ChatSchemaRepo/Services/ChatEventPublisher.cs`
- Transport Interface: `Service/Schemas/ChatSchemaRepo/Services/IWsEventPublisher.cs`
- AWS AppSync Transport: `Service/Schemas/ChatSchemaRepo/Services/AppSyncWsEventPublisher.cs`
- Chat Manager: `Service/Schemas/ChatSchemaRepo/Services/ChatManagerService.cs`
- DI Registration: `Service/Schemas/ChatSchemaRepo/ServiceRepoExtensions.cs`
- Event Types: `Service/Schemas/ChatSchema/DTOs/ChatEventType.g.cs`
- Test Mock: `Service/TestModules/MockWsEventPublisher.cs`

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

- **v1.2** (2025-01-22): Multi-tenant AppSync Events routing and orchestrator pattern
  - **MAJOR**: Implemented multi-tenant AppSync Events routing using `ICallerInfo.Authname`
  - **MAJOR**: Dynamic EventsApi resolution allowing single container to serve multiple auth contexts
  - Documented ChatManagerService as orchestrator coordinating data layer, in-memory state, and events
  - Added comprehensive API endpoint documentation (Chat and Message management)
  - Documented message processing flow with background task architecture
  - Added AppRunner auto-deployment behavior documentation
  - Clarified dual-state management (in-memory + DynamoDB)
  - Documented chat resume capability from persistent storage
  - Added convention-based EventsApi configuration pattern (`{authname}EventsApi`)
  - Updated all event publisher methods to include `ICallerInfo` parameter
- **v1.1** (2025-01-11): Two-layer architecture refactoring
  - Implemented IChatEventPublisher (domain layer) and IWsEventPublisher (transport layer)
  - Simplified ChatManagerService event publishing (83% code reduction)
  - Added platform-independent abstractions
  - Unified configuration with fallback support
  - Updated CloudFormation templates for AppRunner
- **v1.0** (2025-10-11): Initial implementation
  - WebSocket connection and subscription
  - All 6 event types implemented
  - ReactiveUI integration
  - Streaming chunk optimization
  - DataType field added

---

**Last Updated**: 2025-01-22
**Authors**: LazyMagic Team, Claude (Anthropic AI Assistant)
