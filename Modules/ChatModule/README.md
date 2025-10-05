# ChatModule

A real-time AI chat module with dual-deployment architecture: simple AWS App Runner deployment for development/MVP, or production-grade ECS Fargate + ALB deployment for high-scale applications. Features Amazon Bedrock integration, WebSocket-style event publishing via AWS AppSync, and in-memory session management.

## Overview

ChatModule provides conversational AI capabilities for the MagicPets service, enabling users to have interactive conversations with an AI assistant powered by Amazon Bedrock (Claude). The module is designed with a **portable architecture** that supports two deployment modes from the same codebase:

- **Simple Mode (App Runner)**: Single-instance deployment for development, MVP, and low-scale production
- **Production Mode (ECS Fargate + ALB)**: Multi-instance deployment with sticky sessions for high-scale production

## Deployment Modes

ChatModule uses the **same Docker image and codebase** for both deployment modes, ensuring code portability and simplified development.

| Feature | Simple Mode | Production Mode |
|---------|-------------|-----------------|
| **Platform** | AWS App Runner | ECS Fargate + ALB |
| **Use Case** | Dev, MVP, Low-scale | High-scale Production |
| **Instances** | 1 (fixed) | 2-10 (auto-scaling) |
| **Sticky Sessions** | N/A (single instance) | ✅ Yes (ALB cookie-based) |
| **In-Memory State** | ✅ Works | ✅ Works (with stickiness) |
| **High Availability** | ❌ Single point of failure | ✅ Multi-AZ |
| **Custom Domain** | Via Route 53 | ✅ Yes (ALB + ACM) |
| **WAF Integration** | Limited | ✅ Full |
| **VPC Required** | No | Yes |
| **Complexity** | ⭐ Low | ⭐⭐⭐ Medium |
| **Monthly Cost** | ~$30 | ~$150 |
| **Setup Time** | 30 minutes | 4 hours |

### When to Use Each Mode

**Choose Simple Mode if:**
- ✅ Development or testing environment
- ✅ MVP with < 100 concurrent users
- ✅ Budget-constrained deployment
- ✅ Don't need high availability
- ✅ Want fastest time-to-deploy

**Choose Production Mode if:**
- ✅ Production environment with > 100 concurrent users
- ✅ Require high availability and multi-AZ deployment
- ✅ Need true sticky sessions for stateful workloads
- ✅ Want advanced ALB features (WAF, custom routing)
- ✅ Have VPC infrastructure available

### Key Features

- **Real-time AI Conversations**: Powered by Amazon Bedrock (Claude 3 Sonnet)
- **Background Processing**: Asynchronous message processing with queuing
- **Event-Driven Updates**: Real-time status updates via AWS AppSync Events
- **Session Management**: In-memory chat session state with automatic cleanup
- **Multi-User Support**: User isolation via Cognito authentication
- **Stateless Deployment**: Each App Runner instance maintains independent state
- **Health Monitoring**: Built-in health check endpoint for AWS App Runner

### Code Structure

ChatModule follows the standard **Repository Pattern** used throughout MagicPets:

```
ChatModule/                           (Controller Layer - Routing only)
  ├── ChatModuleController.g.cs      - Generated controller with DI
  ├── ChatModuleControllerBase.g.cs  - Generated base with endpoints
  └── README.md                      - This file

ChatSchemaRepo/                       (Repository & Service Layer - All business logic)
  ├── Repos/
  │   ├── ChatRepo.cs                - CRUD operations with in-memory delegation
  │   └── ChatMessagesRepo.cs        - Message operations (create, read)
  ├── Services/
  │   ├── ChatManagerService.cs      - In-memory state + background processing
  │   ├── IChatManagerService.cs     - Service interface
  │   ├── BedrockChat.cs             - AWS Bedrock LLM integration
  │   ├── ILlmClient.cs              - LLM abstraction interface
  │   └── AppSyncEventPublisher.cs   - Real-time event publishing
  └── ServiceRepoExtensions.cs       - DI registration for services

ChatSchema/                           (DTOs & Models)
  └── DTOs/
      ├── Chat.g.cs                  - Chat entity (IItem for DynamoDB)
      ├── ChatMessages.g.cs          - ChatMessages entity (separate table)
      ├── ChatMessage.g.cs           - Message object (in Messages array)
      └── ChatStatus.g.cs            - Enum
```

**Key Design Principle:**
ChatModule (controller) contains **zero business logic** - all logic lives in `ChatSchemaRepo`. This follows the same pattern as `PetRepo`, `CategoryRepo`, etc., ensuring consistent architecture across the entire application.

## Architecture

ChatModule follows the standard **Repository Pattern** architecture used throughout MagicPets, with all business logic contained in `ChatSchemaRepo`. The module supports two deployment architectures from the same codebase.

### Simple Mode Architecture (App Runner)

```
┌─────────────────────────────────────────────────────────────┐
│                      Client Application                      │
└─────────────────┬──────────────────────┬────────────────────┘
                  │                      │
                  │ REST API             │ AppSync Events
                  │ (Request/Response)   │ (Real-time Updates)
                  │                      │
┌─────────────────▼──────────────────────▼────────────────────┐
│                   AWS App Runner Service                      │
│  ┌────────────────────────────────────────────────────────┐  │
│  │      Built-in Load Balancer (NLB + Layer-7 Router)     │  │
│  └────────────────────────┬───────────────────────────────┘  │
│                           │                                   │
│  ┌────────────────────────▼───────────────────────────────┐  │
│  │           ChatAppRunner Container (Single Instance)     │  │
│  │  • Port 8080                                           │  │
│  │  • In-memory state (1 instance = no conflicts)         │  │
│  │  • Keep-alive strategy (prevents scale-down)           │  │
│  │  • Auto-scaling: MinSize=1, MaxSize=1                  │  │
│  └────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

**Characteristics:**
- Fixed single instance deployment
- Built-in load balancer (no configuration needed)
- Keep-alive long-polling prevents premature scale-down
- No sticky sessions needed (only one instance)
- Simplest deployment option

### Production Mode Architecture (ECS Fargate + ALB)

```
┌─────────────────────────────────────────────────────────────┐
│                      Client Application                     │
└─────────────────┬──────────────────────┬────────────────────┘
                  │                      │
                  │ HTTPS (443)          │ AppSync Events
                  │                      │
┌─────────────────▼──────────────────────▼────────────────────┐
│              Application Load Balancer (ALB)                │
│  • Sticky sessions: AWSALB cookie (30 min TTL)              │
│  • Health checks: /ChatModule/health                        │
│  • Custom domain + ACM certificate                          │
│  • WAF integration available                                │
│  • Security Groups: HTTPS from 0.0.0.0/0                    │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  │ Target Group (type: ip, stickiness: enabled)
                  │
┌─────────────────▼────────────────────────────────────────────┐
│                    ECS Fargate Service                       │
│  ┌──────────────────────┐  ┌──────────────────────┐          │
│  │  Fargate Task 1      │  │  Fargate Task 2      │          │
│  │  (Private Subnet A)  │  │  (Private Subnet B)  │  ...     │
│  │  ┌────────────────┐  │  │  ┌────────────────┐  │          │
│  │  │ ChatAppRunner  │  │  │  │ ChatAppRunner  │  │          │
│  │  │ Container      │  │  │  │ Container      │  │          │
│  │  │ • Port 8080    │  │  │  │ • Port 8080    │  │          │
│  │  │ • 1 vCPU       │  │  │  │ • 1 vCPU       │  │          │
│  │  │ • 2 GB RAM     │  │  │  │ • 2 GB RAM     │  │          │
│  │  │ • In-memory OK │  │  │  │ • In-memory OK │  │          │
│  │  └────────────────┘  │  │  └────────────────┘  │          │
│  │  IP: 10.0.1.50      │  │  IP: 10.0.2.50      │            │
│  └──────────────────────┘  └──────────────────────┘          │
│  • Auto-scaling: 2-10 tasks based on CPU                     │
│  • Multi-AZ deployment                                       │
└──────────────────────────────────────────────────────────────┘
```

**Characteristics:**
- Multi-instance deployment (2-10 tasks)
- ALB provides sticky sessions (same client → same task)
- In-memory state works with sticky sessions
- High availability across multiple AZs
- Advanced routing and WAF capabilities

### Shared Container Architecture

Both deployment modes use the **same ChatAppRunner container** with clean layered architecture:

```
┌─────────────────▼──────────────────────▼────────────────────┐
│                   ChatAppRunner Container                   │
│  ┌────────────────────────────────────────────────────────┐ │
│  │           ChatModuleController (Controller Layer)      │ │
│  │  /chat/create  /chat/{id}/message  /chat/{id}/status   │ │
│  │  /chat  /chat/{id}/messages  /chat/{id}                │ │
│  │  /internal/keepalive/{id} (load representation)        │ │
│  └─────────────┬──────────────────────────────────────────┘ │
│                │                                            │
│  ┌─────────────▼──────────────────────────────────────────┐ │
│  │       ChatRepo (ChatSchemaRepo - Repository Layer)     │ │
│  │  • CreateChatAsync, SendMessageAsync, GetChatAsync     │ │
│  │  • UpdateChatAsync, ListChatsAsync, CloseChatAsync     │ │
│  │  • Delegates to ChatManagerService for business logic  │ │
│  └─────────────┬──────────────────────────────────────────┘ │
│                │                                            │
│  ┌─────────────▼──────────────────────────────────────────┐ │
│  │    ChatManagerService (ChatSchemaRepo - Service Layer) │ │
│  │  • In-Memory Chat State (ConcurrentDictionary)         │ │
│  │  • Message Queue (Channel<ChatMessage>)                │ │
│  │  • Background Processing Task per Chat                 │ │
│  │  • Keep-Alive Semaphores (Load Representation)         │ │
│  │  • Automatic Cleanup (30 min timeout)                  │ │
│  └────┬──────────────────────┬──────────────────┬─────────┘ │
│       │                      │                  │           │
│       │ Process Messages     │ Self-Request     │ Publish   │
│       │                      │ Keep-Alive       │ Events    │
│  ┌────▼────────────────┐  ┌──▼─────────────┐ ┌─▼──────────┐ │
│  │ BedrockChat Service │  │ HttpClient     │ │  AppSync   │ │
│  │ (ChatSchemaRepo)    │  │ • POST /keep.. │ │  Publisher │ │
│  │ • Invoke Bedrock    │  │ • Blocks on    │ │  (ChatRepo)│ │
│  │ • Streaming Support │  │   Semaphore    │ │  • Events  │ │
│  └──────────┬──────────┘  │                │ └─┬──────────┘ │
│             │             └────────────────┘   │            │
└─────────────┼──────────────────────────────────┼────────────┘
              │                                  │
    ┌─────────▼─────────┐          ┌────────────▼─────────────┐
    │  Amazon Bedrock   │          │    AWS AppSync Events    │
    │  Runtime API      │          │    (Real-time Channel)   │
    └───────────────────┘          └──────────────────────────┘
```

**Layered Architecture:**
- **Controller Layer** (`ChatModule`): Thin routing layer, handles HTTP requests
- **Repository Layer** (`ChatSchemaRepo`): ChatRepo delegates to services
- **Service Layer** (`ChatSchemaRepo`): ChatManagerService, BedrockChat, AppSyncEventPublisher
- **AWS Integration**: Bedrock, AppSync, DynamoDB (for persistence)

## Key Components

### 1. ChatRepo (Repository Layer)

**Location**: `ChatSchemaRepo/Repos/ChatRepo.cs`
**Type**: Partial class extending `DYDBRepository<Chat>`
**Lifetime**: Scoped (injected into controllers)

Entry point for all chat CRUD operations, following the standard repository pattern:

- **Overridden Methods**: CreateAsync, ReadAsync, UpdateAsync, DeleteAsync, ListAsync
- **Hybrid Architecture**: Delegates to `IChatManagerService` for in-memory operations, calls base methods for DynamoDB persistence
- **DynamoDB Integration**: Persists Chat and ChatMessages entities to DynamoDB
- **Clean Separation**: Orchestrates between in-memory state and persistent storage

### 2. ChatManagerService (Service Layer)

**Location**: `ChatSchemaRepo/Services/ChatManagerService.cs`
**Type**: `IHostedService` (Background Service)
**Lifetime**: Singleton (one per container instance)

Core service that manages chat lifecycle and background processing:

- **In-Memory State**: Stores active chats in `ConcurrentDictionary<string, ConnectionChat>`
- **Message Queuing**: Each chat has a `Channel<ChatMessage>` for async processing
- **Background Processing**: One background task per chat processes messages sequentially
- **Automatic Cleanup**: Timer runs every 5 minutes to close chats inactive for 30+ minutes
- **Thread-Safe**: Uses concurrent collections for multi-user safety
- **Keep-Alive Management**: Maintains semaphores for load representation

**Key Methods**:
- `InitializeChatAsync(callerInfo, chat)` - Creates new in-memory chat and starts background processor
- `ProcessUserMessageAsync(callerInfo, chatId, message)` - Queues user message for processing, returns immediately
- `GetChatByIdAsync(callerInfo, chatId)` - Retrieves chat from in-memory state
- `GetChatHistoryAsync(callerInfo, chatId, page, limit)` - Returns paginated message history from in-memory state
- `UpdateChatAsync(callerInfo, chat)` - Updates in-memory chat state
- `CloseChatAsync(callerInfo, chatId)` - Cleanly shuts down chat and background task
- `GetKeepAliveSemaphore(chatId)` - Returns semaphore for keep-alive endpoint (deprecated - now uses single service-wide semaphore)

### 3. ChatMessagesRepo (Repository Layer)

**Location**: `ChatSchemaRepo/Repos/ChatMessagesRepo.cs`
**Type**: Partial class extending `DYDBRepository<ChatMessages>`
**Lifetime**: Scoped (injected into controllers)

Handles message-specific operations:

- **AddMessageAsync**: Adds user message to chat, delegates to ChatManagerService for processing, persists to DynamoDB
- **GetMessagesAsync**: Returns paginated messages from in-memory (if chat is active) or DynamoDB (if inactive)
- **Hybrid Retrieval**: Attempts in-memory first, falls back to DynamoDB if chat not active

### 4. BedrockChat (Service Layer)

**Location**: `ChatSchemaRepo/Services/BedrockChat.cs`
**Interface**: `ILlmClient`
**Lifetime**: Singleton

Handles AI processing via Amazon Bedrock:

- **Model**: Claude 3 Sonnet (`anthropic.claude-3-sonnet-20240229-v1:0`)
- **Interface**: Implements `ILlmClient` for swappable LLM providers
- **Methods**: `GenerateResponseAsync(conversationHistory)`, `GenerateResponseAsync(userMessage)`
- **Error Handling**: Returns friendly error messages on failure

### 5. AppSyncEventPublisher (Service Layer)

**Location**: `ChatSchemaRepo/Services/AppSyncEventPublisher.cs`
**Lifetime**: Singleton

Publishes real-time events to connected clients:

- **Chat Events**: Message received/completed, status changes, errors
- **Event Types**:
  - `chat_created`
  - `chat_status_changed`
  - `message_received`
  - `message_processing`
  - `message_completed`
  - `chat_closed`
  - `error_occurred`

### 6. ChatModuleAuthorization

**Location**: `ChatModule/ChatModuleAuthorization.cs`
**Type**: Partial class extending generated base

Custom authorization handling:

- **GetLzHeaders**: Captures the `Host` header from incoming requests for keep-alive URL construction
- **HasPermissionAsync**: Authorization logic (currently allows all authenticated users)
- Integrates with LazyMagic `ICallerInfo` pattern

## API Endpoints

All endpoints follow standard REST CRUD patterns and are prefixed with `/ChatModule/`.

### POST /chat

Creates a new chat session.

**Request Body** (Chat object):
```json
{
  "userId": "user-123",
  "status": "active",
  "summary": "Initial chat session"
}
```

**Response** (200 OK):
```json
{
  "id": "chat-uuid",
  "chatId": "chat-uuid",
  "userId": "user-123",
  "status": "active",
  "summary": "Initial chat session",
  "chatMessagesId": "chat-uuid",
  "messageCount": 0,
  "createUtcTick": 638123456789012345,
  "updateUtcTick": 638123456789012345
}
```

### GET /chat/{chatId}

Retrieves a specific chat by ID.

**Response** (200 OK): Returns Chat object (same structure as POST response)

### PUT /chat/{chatId}

Updates an existing chat.

**Request Body**: Chat object with updated fields

**Response** (200 OK): Returns updated Chat object

### DELETE /chat/{chatId}

Closes a chat and releases resources (also deletes associated ChatMessages).

**Response**: 200 OK

### GET /chat

Lists all chats for the authenticated user.

**Query Parameters**:
- `limit` (optional): Number of chats to return

**Response** (200 OK):
```json
[
  {
    "id": "chat-1",
    "chatId": "chat-1",
    "userId": "user-123",
    "status": "active",
    "summary": "Chat about pets",
    "messageCount": 5,
    ...
  },
  {
    "id": "chat-2",
    "chatId": "chat-2",
    "userId": "user-123",
    "status": "closed",
    "summary": "Previous conversation",
    "messageCount": 12,
    ...
  }
]
```

### POST /chat/{chatId}/messages

Adds a message to a chat and queues it for AI processing.

**Request Body** (ChatMessage object):
```json
{
  "role": "user",
  "content": "Tell me about magic pets"
}
```

**Response** (200 OK): Returns the ChatMessage object with generated messageId and timestamp

### GET /chat/{chatId}/messages

Retrieves paginated message history for a chat.

**Query Parameters**:
- `page` (optional): Page number (default: 1)
- `limit` (optional): Messages per page (default: 50, max: 100)

**Response** (200 OK):
```json
[
  {
    "messageId": "uuid-1",
    "role": "user",
    "content": "Hello",
    "timestamp": "2025-10-04T12:00:00Z"
  },
  {
    "messageId": "uuid-2",
    "role": "assistant",
    "content": "Hi! How can I help?",
    "timestamp": "2025-10-04T12:00:05Z"
  }
]
```

### GET /health

Health check endpoint for AWS App Runner monitoring.

**Response** (200 OK):
```json
{
  "status": "healthy",
  "timestamp": "2025-10-03T12:00:00Z",
  "version": "1.0.0"
}
```

## How It Works

### Chat Lifecycle

1. **Creation** (POST /chat):
   ```
   Client → POST /chat (Chat object)
   ↓
   ChatModuleController.AddChatAsync()
   ↓
   ChatRepo.CreateAsync(callerInfo, chat)
   ↓
   ChatManagerService.InitializeChatAsync(callerInfo, chat)
   ↓
   Creates ConnectionChat with:
   - Unique chatId
   - Message queue (Channel)
   - Background processing task
   - CallerInfo (including Host header)
   ↓
   Starts ProcessChatMessagesAsync() background task
   ↓
   If first chat: Initiates single keep-alive long-polling request
   ↓
   Returns initialized Chat object
   ↓
   ChatRepo persists Chat to DynamoDB (via base.CreateAsync)
   ↓
   ChatRepo creates empty ChatMessages record in DynamoDB
   ```

2. **Message Processing** (POST /chat/{chatId}/messages):
   ```
   Client → POST /chat/{chatId}/messages (ChatMessage object)
   ↓
   ChatModuleController.AddChatMessageAsync()
   ↓
   ChatMessagesRepo.AddMessageAsync(callerInfo, chatId, message)
   ↓
   ChatManagerService.ProcessUserMessageAsync(callerInfo, chatId, message)
   ↓
   Adds user message to in-memory chat.History
   ↓
   Queues message in chat.MessageQueue (Channel)
   ↓
   Returns user message immediately (async processing)
   ↓
   ChatMessagesRepo loads ChatMessages from DynamoDB
   ↓
   Appends message to Messages array
   ↓
   Persists updated ChatMessages to DynamoDB

   Background Task (ProcessChatMessagesAsync):
   ↓
   Reads message from chat.MessageQueue
   ↓
   AppSyncEventPublisher.PublishChatEventAsync("message_received")
   ↓
   ILlmClient.GenerateResponseAsync(chat.History)  // Bedrock call
   ↓
   Creates assistant ChatMessage
   ↓
   Adds to in-memory chat.History
   ↓
   Updates Chat.Summary if needed
   ↓
   AppSyncEventPublisher.PublishChatEventAsync("message_completed")
   ```

3. **Event Flow**:
   ```
   Background Processor
   ↓
   AppSyncEventPublisher.PublishChatEventAsync()
   ↓
   Creates event payload
   ↓
   Publishes to AppSync Events API
   ↓
   Connected clients receive real-time update via WebSocket
   ```

4. **Cleanup**:
   ```
   Timer (every 5 minutes)
   ↓
   CleanupExpiredChats()
   ↓
   Finds chats with LastActivityAt > 30 minutes
   ↓
   For each expired chat:
     - Cancel background task
     - Complete message queue
     - Remove from _chats dictionary
     - Dispose resources
   ↓
   If last chat closed: Release keep-alive semaphore
   ```

## Load Representation Strategy

### The Problem

AWS App Runner scales instances based solely on **concurrent HTTP requests**. When HTTP requests complete, the request count drops to zero, even if background tasks are still running. This creates a critical issue for ChatModule:

1. Client sends `POST /chat/create` → Request completes immediately
2. Background task processes messages for 2-5 seconds (Bedrock API call)
3. App Runner sees: **0 concurrent requests** = Instance appears idle
4. App Runner may scale down and **terminate the instance**, killing active background tasks

### The Solution: Single Keep-Alive Long-Polling

ChatModule implements a **single service-wide keep-alive pattern** that maintains one long-polling HTTP request when any chats are active:

```
Service Lifecycle:
1. First POST /chat → Creates chat → Returns immediately
2. ChatManagerService initiates single keep-alive task (if not already running)
3. Keep-alive task calls POST /ChatModule/internal/keepalive
4. Internal endpoint calls semaphore.WaitAsync() → BLOCKS REQUEST
5. Background tasks process messages while keep-alive request is held open
6. Last chat closes → CloseChatAsync() releases semaphore
7. Keep-alive request completes → HTTP request count decrements to 0
8. Service ready to scale down if idle

Result: 1+ Active Chats = 1 Keep-Alive Request (plus normal user API traffic)
```

### Implementation Details

**ChatManagerService**:
- `_keepAliveSemaphore`: Single `SemaphoreSlim(0, 1)` for entire service
- `_keepAliveTask`: Single background Task for all chats
- `InitializeChatAsync()`: Starts keep-alive task if first chat (_chats.Count == 1)
- `InitiateKeepAliveAsync()`: POSTs to `https://{Host}/ChatModule/internal/keepalive`
- `CloseChatAsync()`: Releases semaphore if last chat (_chats.Count == 0)
- `GetServiceHost()`: Reads Host from CallerInfo.Headers, falls back to localhost:8080

**Host Header Resolution**:
- `ChatModuleAuthorization.GetLzHeaders()`: Captures `Host` header from HttpRequest
- Stored in `CallerInfo.Headers["Host"]`
- `GetServiceHost()` constructs URL: `http://localhost:8080` or `https://{domain}`
- No infrastructure configuration needed (Host header is standard HTTP/1.1)

**InternalController** (`/ChatModule/internal/keepalive`):
- Calls `await _chatManagerService.GetKeepAliveSemaphore().WaitAsync(cancellationToken)`
- Blocks HTTP request until last chat closes
- Returns 200 OK when service has no active chats

**HttpClient Configuration**:
- Named client: "KeepAlive"
- Timeout: 60 minutes (longer than 30-min chat timeout)
- Registered in DI: `services.AddHttpClient("KeepAlive")`

### Load Representation

This pattern efficiently represents instance load to App Runner:

| Active Chats | Keep-Alive Requests | User API Requests | Total Concurrent Requests |
|--------------|---------------------|-------------------|---------------------------|
| 0            | 0                   | 0                 | 0                         |
| 1            | 1                   | ~2-5              | ~3-6                      |
| 50           | 1                   | ~10-20            | ~11-21                    |
| 100          | 1                   | ~20-40            | ~21-41                    |

**MaxConcurrency Calculation**:
- Keep-alive request: 1 (constant when chats exist)
- User API requests: ~50 peak (POST /chat, POST /messages, GET /messages, etc.)
- **Total MaxConcurrency: 60** (updated - much lower than previous per-chat approach)

### Benefits

✅ **Prevents Premature Scale-Down**: Instance won't terminate while any chats are active
✅ **Efficient**: Single keep-alive request instead of N requests (one per chat)
✅ **Minimal Overhead**: Only 1 HTTP connection used for keep-alive
✅ **No External Dependencies**: Self-contained, uses standard HTTP
✅ **Dynamic Host Resolution**: Uses standard Host header, no infrastructure setup needed
✅ **Works with App Runner Model**: Legitimate use of request-based scaling

### Trade-offs

⚠️ **Not Proportional Load**: 1 chat or 100 chats = same keep-alive overhead (but this is acceptable)
⚠️ **MaxConcurrency Planning**: Must still account for user API traffic
⚠️ **Not True Custom Metrics**: Better solutions exist (ECS Fargate with CloudWatch custom metrics)
⚠️ **Still Instance-Local**: Doesn't solve multi-instance state sharing

## Implementation Roadmap

### Current Status: Simple Mode (App Runner)

ChatModule is currently implemented for **Simple Mode (App Runner)** with keep-alive load representation.

**✅ Completed:**
- Standard ASP.NET Core 8.0 architecture (portable)
- Docker containerization
- Keep-alive long-polling strategy
- ChatManagerService with in-memory state
- Background message processing
- AppSync event publishing
- Health check endpoint
- Cognito authentication

**✅ Portability:**
- No App Runner-specific code
- Environment variable configuration
- Standard port 8080
- Compatible with both App Runner and Fargate

### Planned: Production Mode (ECS Fargate + ALB)

**Phase 1: Planning & Design** *(Current)*
- Document dual-deployment strategy
- Design SAM template snippets
- Plan LazyMagic configuration updates
- Design deployment tooling

**Phase 2: Finalize Simple Mode** *(Next)*
- Test keep-alive implementation
- Validate App Runner deployment
- Performance baseline
- Documentation updates

**Phase 3: Design Production Mode**
- Create SAM template for Fargate + ALB
- Design LazyMagic integration
- VPC networking design
- Deployment mode selection mechanism

**Phase 4: Implement Production Mode**
- Implement Fargate + ALB templates
- Update LazyMagic code generation
- VPC setup (if needed)
- ACM certificate provisioning
- Test sticky sessions
- Performance validation

**Phase 5: Update Deployment Tooling**
- Dual-mode deployment scripts
- LazyMagicCLI updates
- Deployment guide
- Migration documentation

**Estimated Timeline:** 5 days (38 hours) of development effort

### Migration Path

**From Simple to Production:**
1. Provision VPC infrastructure (public/private subnets, NAT gateways)
2. Request ACM certificate for custom domain
3. Update `deployment-mode.yaml` configuration
4. Build Docker image (same image works for both)
5. Deploy with Production mode flag
6. Configure DNS to point to ALB
7. Test sticky sessions and multi-instance behavior
8. Gradually increase traffic

**From Production to Simple:**
- Reverse process (typically for cost reduction in non-production environments)

## Cost Analysis

### Simple Mode Costs (Monthly)

| Resource | Quantity | Unit Cost | Monthly Cost |
|----------|----------|-----------|--------------|
| App Runner (1 vCPU, 2 GB) | 1 instance @ 100% | ~$0.034/hour | ~$25 |
| Data Transfer Out | ~100 GB | $0.09/GB | ~$9 |
| CloudWatch Logs | ~5 GB | $0.50/GB | ~$2.50 |
| **Total** | | | **~$36/month** |

**Scaling:** Fixed cost (single instance)

### Production Mode Costs (Monthly)

| Resource | Quantity | Unit Cost | Monthly Cost |
|----------|----------|-----------|--------------|
| Application Load Balancer | 1 ALB | ~$16/month | ~$16 |
| ALB Data Processing | ~500 GB | $0.008/GB | ~$4 |
| Fargate Tasks (1 vCPU, 2 GB) | 2 tasks @ 100% | ~$30/month each | ~$60 |
| NAT Gateway | 2 (multi-AZ) | ~$32/month each | ~$64 |
| Data Transfer (NAT) | ~100 GB | $0.045/GB | ~$4.50 |
| CloudWatch Logs | ~10 GB | $0.50/GB | ~$5 |
| **Total (2 tasks)** | | | **~$153/month** |

**Scaling:**
- 3 tasks: ~$183/month (+$30 per task)
- 5 tasks: ~$243/month
- 10 tasks: ~$393/month

**Cost Optimization Options:**
- Use FARGATE_SPOT for dev/test (60-70% savings)
- Reduce NAT Gateway costs with VPC endpoints
- Optimize CloudWatch log retention
- Use Reserved Capacity for predictable workloads

### Cost Comparison Summary

| Scenario | Simple Mode | Production Mode | Difference |
|----------|-------------|-----------------|------------|
| **Dev/Test** | $36/month | $153/month | +325% |
| **Low Traffic** | $36/month | $153/month | +325% |
| **Medium Traffic (5 tasks)** | $36/month | $243/month | +575% |
| **High Traffic (10 tasks)** | N/A (1 instance limit) | $393/month | — |

**Recommendation:**
- Use **Simple Mode** for development, staging, and low-traffic production (<100 concurrent users)
- Use **Production Mode** for high-traffic production requiring HA and scalability

## Configuration

### Environment Variables

Required configuration in `appsettings.json` or environment variables:

```json
{
  "AWS": {
    "Cognito": {
      "UserPoolId": "us-east-1_xxxxx",
      "Region": "us-east-1"
    },
    "Bedrock": {
      "ModelId": "anthropic.claude-3-sonnet-20240229-v1:0"
    },
    "AppSync": {
      "EventApiId": "xxxxxxxxxxxxxxxxxxxxx"
    }
  }
}
```

### IAM Permissions

The App Runner instance role requires:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "bedrock:InvokeModel",
        "bedrock:InvokeModelWithResponseStream"
      ],
      "Resource": "arn:aws:bedrock:*::foundation-model/anthropic.claude-*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "appsync:PutEvents"
      ],
      "Resource": "arn:aws:appsync:*:*:apis/*/events"
    },
    {
      "Effect": "Allow",
      "Action": [
        "cognito-idp:GetUser"
      ],
      "Resource": "arn:aws:cognito-idp:*:*:userpool/*"
    }
  ]
}
```

## Dependencies

### NuGet Packages

- `AWSSDK.BedrockRuntime` - Amazon Bedrock API client
- `AWSSDK.AppSync` - AWS AppSync Events client
- `AWSSDK.CognitoIdentityProvider` - Cognito authentication
- `ChatSchema` - Generated DTOs and models
- `LazyMagic.*` - Framework utilities

### AWS Services

- **Amazon Bedrock**: AI model inference (Claude 3 Sonnet)
- **AWS AppSync Events**: Real-time event publishing
- **Amazon Cognito**: User authentication and authorization
- **AWS App Runner**: Container hosting platform

## Deployment

**Current Status:**
- ✅ **Simple Mode (App Runner)**: Fully implemented and documented below
- 🚧 **Production Mode (ECS Fargate + ALB)**: Planned (see Implementation Roadmap section)

### Simple Mode: App Runner Deployment

#### App Runner Configuration

The module is deployed as part of the ChatAppRunner container:

```yaml
# sam.service.apprunner.yaml
InstanceConfiguration:
  Cpu: 1024      # 1 vCPU
  Memory: 2048   # 2 GB
  InstanceRoleArn: !GetAtt ChatAppRunnerInstanceRole.Arn

AutoScalingConfiguration:
  MaxConcurrency: 60   # Updated for single keep-alive approach (1 keep-alive + ~50 user requests)
  MaxSize: 1           # Fixed single instance for Simple Mode
  MinSize: 1
```

#### Docker Build

The same Docker image works for both deployment modes:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY ./publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ChatAppRunner.dll"]
```

**Build command:**
```bash
cd Service
Deploy-DockerAws -Service ChatAppRunner -Environment dev
```

#### Health Check

App Runner monitors the `/ChatModule/health` endpoint:

```yaml
HealthCheckConfiguration:
  Protocol: HTTP
  Path: /ChatModule/health
  Interval: 10
  Timeout: 5
  HealthyThreshold: 2
  UnhealthyThreshold: 3
```

### Production Mode: ECS Fargate + ALB (Planned)

**Status:** 🚧 In Planning Phase

Production Mode deployment using ECS Fargate with Application Load Balancer is currently in the design phase. See the **Implementation Roadmap** section above for details on the planned architecture and timeline.

**Key Features (Planned):**
- Multi-instance deployment (2-10 Fargate tasks)
- ALB with sticky sessions (cookie-based affinity)
- VPC networking with private subnets
- Custom domain with ACM certificate
- High availability across multiple AZs
- Advanced auto-scaling based on CPU/memory

**Prerequisites (When Implemented):**
- VPC with public/private subnets
- NAT Gateways
- ACM certificate for custom domain
- Same Docker image as Simple Mode

For now, use **Simple Mode (App Runner)** for all deployments. Production Mode will be available in a future update.

## Usage Examples

### Client SDK (JavaScript/TypeScript)

```typescript
import { ChatApi } from '@/api/chat';

const chatApi = new ChatApi(baseUrl, httpClient);

// Create new chat
const chat = await chatApi.CreateChatAsync({
  userId: currentUserId,
  status: "active",
  summary: "New conversation"
});

const chatId = chat.chatId;

// Subscribe to real-time events
eventClient.subscribe(`chat/${chatId}`, (event) => {
  if (event.eventType === 'message_completed') {
    const message = event.data;
    console.log('AI Response:', message.content);
  }
});

// Send message
const message = await chatApi.createChatMessageAsync(chatId, {
  role: "user",
  content: "Tell me more about MagicPets"
});

// Get message history
const messages = await chatApi.getChatMessagesAsync(
  chatId,
  1,  // page
  50  // limit
);

// Close chat when done
await chatApi.deleteChatAsync(chatId);
```

### Client SDK (C# / Blazor)

```csharp
var chatApi = serviceProvider.GetRequiredService<IChatApi>();

// Create chat
var chat = await chatApi.AddChatAsync(new Chat
{
    UserId = currentUserId,
    Status = ChatStatus.Active,
    Summary = "New conversation"
});

var chatId = chat.ChatId;

// Send message
var message = await chatApi.AddChatMessageAsync(
    chatId,
    new ChatMessage
    {
        Role = "user",
        Content = "Tell me more"
    });

// Get chat details
var chatDetails = await chatApi.GetChatAsync(chatId);
Console.WriteLine($"Chat has {chatDetails.MessageCount} messages");

// Get messages
var messages = await chatApi.GetChatMessagesAsync(
    chatId,
    page: 1,
    limit: 50);

// Close
await chatApi.DeleteChatAsync(chatId);
```

## Limitations and Considerations

### Hybrid In-Memory + DynamoDB State

- **In-Memory for Active Chats**: Active chats use in-memory state for fast processing
- **DynamoDB for Persistence**: All chats and messages persisted to DynamoDB
- **Automatic Rehydration**: Inactive chats retrieved from DynamoDB when accessed
- **Scalability**: Memory usage grows with active chats only (mitigated by 30-min cleanup)
- **Container Restarts**: Chat history preserved in DynamoDB, in-memory state rebuilt on demand

### Keep-Alive Load Representation

- **Solved**: Single keep-alive long-polling prevents premature scale-down
- **MaxConcurrency**: Set to 60 (1 keep-alive + ~50 user API requests)
- **Minimal Overhead**: Only 1 HTTP connection used regardless of chat count
- **Dynamic Host Resolution**: Uses standard Host header, no infrastructure configuration needed
- **Timeout Management**: Keep-alive request timeout at 60 minutes, longer than 30-min chat timeout

### Recommendations

✅ **DynamoDB Persistence**: Already implemented - all chats and messages persisted automatically
✅ **Hybrid Architecture**: Fast in-memory processing for active chats, persistent storage for inactive chats
✅ **Auto-Rehydration**: Chats automatically loaded from DynamoDB when accessed

For high-availability production deployments:
- Consider Production Mode (ECS Fargate + ALB) for multi-AZ deployment
- Use ElastiCache/Redis for shared session state across multiple instances
- Implement real-time event delivery via AppSync subscriptions

### Performance

- **Concurrent Chats**: Supports 100+ concurrent chats per instance
- **Message Latency**: ~2-5 seconds for Bedrock response
- **Auto-scaling**: Scales based on CPU/memory (not chat count)

## Future Enhancements

- [x] **Persistent storage**: ✅ Implemented - DynamoDB persistence for Chat and ChatMessages
- [x] **Standard CRUD API**: ✅ Implemented - REST endpoints following repository pattern
- [x] **LLM Abstraction**: ✅ Implemented - ILlmClient interface for swappable providers
- [x] **Single Keep-Alive**: ✅ Implemented - Service-wide keep-alive optimization
- [x] **Dynamic Host Resolution**: ✅ Implemented - Uses standard Host header
- [ ] **Streaming responses**: Real-time token delivery via AppSync Events
- [ ] **Chat history search**: Full-text search across message content using DynamoDB queries
- [ ] **Multi-model support**: GPT-4, Claude Opus, Gemini via ILlmClient implementations
- [ ] **Rate limiting**: Per-user throttling and quotas
- [ ] **Analytics**: Usage metrics, conversation analytics, user insights
- [ ] **Context window management**: Intelligent truncation and summarization
- [ ] **Conversation summarization**: Auto-summarize long conversations into Chat.Summary

## Troubleshooting

### Common Issues

**Chat not found (404)**:
- Chat may have expired (30-min timeout)
- Request routed to different App Runner instance
- Check chat existence before operations

**Messages not processing**:
- Check CloudWatch logs for Bedrock errors
- Verify IAM permissions on instance role
- Confirm Bedrock model availability in region

**Events not received**:
- Verify AppSync Events API ID configuration
- Check AppSync subscription filters
- Confirm client WebSocket connection

### Logging

All components log to CloudWatch:
- **Log Group**: `/aws/apprunner/{stack-name}-ChatAppRunner`
- **Retention**: 1 day (configurable)
- **Log Level**: Info (Debug for troubleshooting)

## Summary

ChatModule provides AI-powered conversational capabilities with a **dual-deployment architecture** designed for flexibility and scalability:

### ✅ Currently Available: Simple Mode (App Runner)
- **Perfect for:** Development, MVP, staging, low-traffic production
- **Deployment time:** 30 minutes
- **Cost:** ~$36/month
- **Complexity:** Low
- **Instances:** 1 (fixed)
- **Code:** Fully portable ASP.NET Core 8.0
- **Innovation:** Keep-alive long-polling prevents premature scale-down

### 🚧 Planned: Production Mode (ECS Fargate + ALB)
- **Perfect for:** High-scale production, high-availability requirements
- **Deployment time:** 4 hours (first time)
- **Cost:** ~$153/month (2 tasks) to ~$393/month (10 tasks)
- **Complexity:** Medium
- **Instances:** 2-10 (auto-scaling)
- **Code:** Same Docker image as Simple Mode
- **Benefits:** True sticky sessions, multi-AZ, custom domain, WAF

### Key Design Principles

1. **Portability First**: Same codebase and Docker image for both modes
2. **Progressive Enhancement**: Start simple, scale when needed
3. **Cost-Effective**: Pay only for what you need
4. **Battle-Tested**: Standard ASP.NET Core patterns
5. **Cloud-Native**: Designed for AWS from the ground up

### Next Steps

**For Development:**
1. Deploy using Simple Mode (App Runner)
2. Test AI conversations with Amazon Bedrock
3. Validate AppSync event publishing
4. Monitor performance and costs

**For Production:**
1. Start with Simple Mode for initial launch
2. Monitor traffic and user growth
3. When ready to scale, migrate to Production Mode
4. Follow the Implementation Roadmap (5-day effort)

**Current Recommendation:** Use **Simple Mode** for all environments until traffic exceeds 100 concurrent users or high availability is required.

## Architectural Patterns & Benefits

### Repository Pattern Implementation

ChatModule follows the **same architectural pattern** as all other modules in MagicPets:

```
Controller → Repository → Service → AWS SDK
```

**Benefits:**
- ✅ **Consistency**: Same pattern as PetRepo, CategoryRepo, etc.
- ✅ **Testability**: Business logic isolated in ChatSchemaRepo
- ✅ **Reusability**: Services can be used from multiple repos
- ✅ **Separation of Concerns**: Clear boundaries between layers
- ✅ **DI-Friendly**: All components registered in ServiceRepoExtensions

### Why Business Logic is in ChatSchemaRepo (Not ChatModule)

**Traditional Approach (Anti-pattern):**
```
ChatModule/
  ├── ChatController.cs
  ├── ChatManager.cs           ❌ Business logic in module
  ├── ChatManagerService.cs    ❌ Business logic in module
  └── BedrockChat.cs           ❌ Business logic in module
```

**MagicPets Approach (Repository Pattern):**
```
ChatModule/
  └── ChatModuleController.g.cs   ✅ Routing only

ChatSchemaRepo/
  ├── Repos/ChatRepo.cs           ✅ Repository pattern
  └── Services/                   ✅ Business logic isolated
      ├── ChatManagerService.cs
      ├── BedrockChat.cs
      └── AppSyncEventPublisher.cs
```

**Why This Matters:**
1. **Reusability**: ChatManagerService can be used by other modules/repos
2. **Testing**: Business logic can be tested without HTTP controllers
3. **Consistency**: Follows established patterns across the entire codebase
4. **Portability**: Repo layer is independent of deployment mode
5. **Clean Boundaries**: Clear separation between routing and business logic

### Generated vs Custom Code

**Auto-Generated** (Do not modify):
- `ChatModuleController.g.cs` - Controller with DI constructor
- `ChatModuleControllerBase.g.cs` - API endpoints, routes
- `ChatRepo.g.cs` - Base repository with standard CRUD (will be generated)
- `Chat.g.cs`, `ChatMessage.g.cs` - DTOs from OpenAPI schema

**Custom Code** (Developer-maintained):
- `ChatRepo.cs` - Partial class extending ChatRepo.g.cs with custom methods
- `ChatManagerService.cs` - Core business logic
- `BedrockChat.cs` - AWS Bedrock integration
- `AppSyncEventPublisher.cs` - Event publishing logic
- `ServiceRepoExtensions.cs` - DI registration

### Integration with LazyMagic Framework

ChatModule demonstrates **advanced LazyMagic patterns**:

1. **OpenAPI-Driven**: All endpoints defined in `openapi.chat.yaml`
2. **Schema-First**: DTOs generated from `openapi.chat-schema.yaml`
3. **x-lz-gencall**: Routes to `ChatRepo.*` methods (e.g., `ChatRepo.CreateChatAsync`)
4. **x-lz-genrepo**: Triggers ChatRepo.g.cs generation with DynamoDB support
5. **Partial Classes**: Custom logic extends generated code seamlessly
6. **IItem Interface**: Enables DynamoDB persistence with zero boilerplate

**Result**: ~90% of code is auto-generated, developers only write core business logic.

## License

Copyright © 2025 MagicPets. All rights reserved.
