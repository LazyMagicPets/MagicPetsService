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
  │   └── ChatRepo.cs                - Partial class with custom API methods
  ├── Services/
  │   ├── ChatManagerService.cs      - In-memory state + background processing
  │   ├── BedrockChat.cs             - AWS Bedrock LLM integration
  │   └── AppSyncEventPublisher.cs   - Real-time event publishing
  └── ServiceRepoExtensions.cs       - DI registration for services

ChatSchema/                           (DTOs & Models)
  └── DTOs/
      ├── Chat.g.cs                  - Chat entity (IItem for DynamoDB)
      ├── ChatMessage.g.cs           - Message entity
      ├── ChatStatus.g.cs            - Enum
      └── *Request/*Response.g.cs    - API DTOs
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
│                      Client Application                      │
└─────────────────┬──────────────────────┬────────────────────┘
                  │                      │
                  │ HTTPS (443)          │ AppSync Events
                  │                      │
┌─────────────────▼──────────────────────▼────────────────────┐
│              Application Load Balancer (ALB)                  │
│  • Sticky sessions: AWSALB cookie (30 min TTL)               │
│  • Health checks: /ChatModule/health                         │
│  • Custom domain + ACM certificate                           │
│  • WAF integration available                                 │
│  • Security Groups: HTTPS from 0.0.0.0/0                     │
└─────────────────┬────────────────────────────────────────────┘
                  │
                  │ Target Group (type: ip, stickiness: enabled)
                  │
┌─────────────────▼────────────────────────────────────────────┐
│                    ECS Fargate Service                        │
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
│  │  IP: 10.0.1.50      │  │  IP: 10.0.2.50      │          │
│  └──────────────────────┘  └──────────────────────┘          │
│  • Auto-scaling: 2-10 tasks based on CPU                     │
│  • Multi-AZ deployment                                        │
└───────────────────────────────────────────────────────────────┘
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
│                   ChatAppRunner Container                     │
│  ┌────────────────────────────────────────────────────────┐  │
│  │           ChatModuleController (Controller Layer)       │  │
│  │  /chat/create  /chat/{id}/message  /chat/{id}/status  │  │
│  │  /chat  /chat/{id}/messages  /chat/{id}               │  │
│  │  /internal/keepalive/{id} (load representation)        │  │
│  └─────────────┬──────────────────────────────────────────┘  │
│                │                                              │
│  ┌─────────────▼──────────────────────────────────────────┐  │
│  │       ChatRepo (ChatSchemaRepo - Repository Layer)      │  │
│  │  • CreateChatAsync, SendMessageAsync, GetChatAsync     │  │
│  │  • UpdateChatAsync, ListChatsAsync, CloseChatAsync     │  │
│  │  • Delegates to ChatManagerService for business logic  │  │
│  └─────────────┬──────────────────────────────────────────┘  │
│                │                                              │
│  ┌─────────────▼──────────────────────────────────────────┐  │
│  │    ChatManagerService (ChatSchemaRepo - Service Layer)  │  │
│  │  • In-Memory Chat State (ConcurrentDictionary)         │  │
│  │  • Message Queue (Channel<ChatMessage>)                │  │
│  │  • Background Processing Task per Chat                 │  │
│  │  • Keep-Alive Semaphores (Load Representation)         │  │
│  │  • Automatic Cleanup (30 min timeout)                  │  │
│  └────┬──────────────────────┬──────────────────┬─────────┘  │
│       │                      │                  │             │
│       │ Process Messages     │ Self-Request     │ Publish     │
│       │                      │ Keep-Alive       │ Events      │
│  ┌────▼────────────────┐  ┌──▼─────────────┐ ┌─▼──────────┐  │
│  │ BedrockChat Service │  │ HttpClient     │ │  AppSync   │  │
│  │ (ChatSchemaRepo)    │  │ • POST /keep.. │ │  Publisher │  │
│  │ • Invoke Bedrock    │  │ • Blocks on    │ │  (ChatRepo)│  │
│  │ • Streaming Support │  │   Semaphore    │ │  • Events  │  │
│  └──────────┬──────────┘  │                │ └─┬──────────┘  │
│             │              └────────────────┘   │             │
└─────────────┼─────────────────────────────────┼─────────────┘
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
**Type**: Partial class extending auto-generated `ChatRepo`
**Lifetime**: Transient (injected into controllers)

Entry point for all chat operations, following the standard repository pattern:

- **Custom API Methods**: CreateChatAsync, SendMessageAsync, GetChatStatusAsync, GetChatAsync, UpdateChatAsync, ListChatsAsync, CloseChatAsync, GetChatMessagesAsync
- **Delegation**: All methods delegate to `ChatManagerService` for business logic
- **Persistence Ready**: Extends `DYDBRepository<Chat>` for future DynamoDB integration
- **Clean Separation**: Isolates business logic from controller layer

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
- `CreateChatAsync(callerInfo, request)` - Creates new chat and starts background processor
- `SendMessageAsync(callerInfo, chatId, request)` - Queues user message for processing
- `GetChatStatusAsync(callerInfo, chatId)` - Returns current chat state
- `GetChatAsync(callerInfo, chatId)` - Retrieves chat by ID
- `UpdateChatAsync(callerInfo, chatId, request)` - Updates chat metadata/status
- `ListChatsAsync(callerInfo, page, limit, status)` - Lists user's chats with pagination
- `GetChatMessagesAsync(callerInfo, chatId, page, limit)` - Paginated message history
- `CloseChatAsync(callerInfo, chatId)` - Cleanly shuts down chat and background task

### 3. BedrockChat (Service Layer)

**Location**: `ChatSchemaRepo/Services/BedrockChat.cs`

**Interface**: `IBedrockChat`
**Lifetime**: Singleton

Handles AI processing via Amazon Bedrock:

- **Model**: Claude 3 Sonnet (`anthropic.claude-3-sonnet-20240229-v1:0`)
- **Standard Processing**: `ProcessMessageAsync()` - Single request/response
- **Streaming Processing**: `ProcessMessageStreamAsync()` - Chunked responses with events
- **Error Handling**: Returns friendly error messages on failure

### 4. AppSyncEventPublisher (Service Layer)

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

### 5. CognitoAuthenticationMiddleware

Custom middleware for Cognito JWT validation:

- Validates JWT tokens from Cognito User Pools
- Extracts user identity (`sub` claim → `LzUserId`)
- Enforces authentication on protected endpoints
- Integrates with LazyMagic `ICallerInfo` pattern

## API Endpoints

All endpoints are prefixed with `/ChatModule/`.

### POST /chat/create

Creates a new chat and starts processing the initial message.

**Request**:
```json
{
  "initialMessage": "Hello, how can you help me today?",
  "chatMetadata": {
    "source": "web",
    "language": "en"
  }
}
```

**Response** (201 Created):
```json
{
  "chat": {
    "chatId": "uuid",
    "userId": "cognito-user-id",
    "status": "processing",
    "createdAt": "2025-10-03T12:00:00Z",
    "lastActivityAt": "2025-10-03T12:00:00Z"
  }
}
```

### POST /chat/{chatId}/message

Sends a message to an existing chat.

**Request**:
```json
{
  "content": "Tell me more about that.",
  "messageMetadata": {}
}
```

**Response** (200 OK):
```json
{
  "message": {
    "messageId": "uuid",
    "chatId": "chat-uuid",
    "role": "user",
    "content": "Tell me more about that.",
    "timestamp": "2025-10-03T12:01:00Z"
  },
  "chat": {
    "chatId": "chat-uuid",
    "userId": "cognito-user-id",
    "status": "processing",
    "createdAt": "2025-10-03T12:00:00Z",
    "lastActivityAt": "2025-10-03T12:01:00Z"
  }
}
```

### GET /chat/{chatId}/status

Gets the current status of a chat.

**Response** (200 OK):
```json
{
  "chat": {
    "chatId": "chat-uuid",
    "userId": "cognito-user-id",
    "status": "active",
    "createdAt": "2025-10-03T12:00:00Z",
    "lastActivityAt": "2025-10-03T12:05:00Z"
  },
  "messageCount": 12,
  "lastMessage": {
    "messageId": "uuid",
    "chatId": "chat-uuid",
    "role": "assistant",
    "content": "I can help with...",
    "timestamp": "2025-10-03T12:05:00Z"
  }
}
```

### GET /chat/{chatId}/messages

Retrieves paginated message history.

**Query Parameters**:
- `page` (optional): Page number (default: 1)
- `limit` (optional): Messages per page (default: 50, max: 100)

**Response** (200 OK):
```json
{
  "messages": [
    {
      "messageId": "uuid",
      "chatId": "chat-uuid",
      "role": "user",
      "content": "Hello",
      "timestamp": "2025-10-03T12:00:00Z"
    },
    {
      "messageId": "uuid",
      "chatId": "chat-uuid",
      "role": "assistant",
      "content": "Hi! How can I help?",
      "timestamp": "2025-10-03T12:00:05Z"
    }
  ],
  "pagination": {
    "page": 1,
    "limit": 50,
    "totalMessages": 2,
    "hasMore": false
  }
}
```

### DELETE /chat/{chatId}

Closes a chat and releases resources.

**Response**: 204 No Content

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

1. **Creation**:
   ```
   Client → POST /chat/create
   ↓
   ChatModuleController.ChatModuleCreateChatAsync()
   ↓
   ChatRepo.CreateChatAsync()
   ↓
   ChatManagerService.CreateChatAsync()
   ↓
   Creates ConnectionChat with:
   - Unique chatId
   - Message queue (Channel)
   - Background processing task
   - Keep-alive semaphore
   ↓
   Starts ProcessChatMessagesAsync() task
   ↓
   Initiates keep-alive long-polling request
   ↓
   Returns chat info to client
   ```

2. **Message Processing**:
   ```
   Client → POST /chat/{id}/message
   ↓
   ChatModuleController.ChatModuleSendMessageAsync()
   ↓
   ChatRepo.SendMessageAsync()
   ↓
   ChatManagerService.SendMessageAsync()
   ↓
   Adds user message to chat.History
   ↓
   Queues message in chat.MessageQueue
   ↓
   Returns immediately (async processing)

   Background Task:
   ↓
   ProcessChatMessagesAsync() reads from queue
   ↓
   AppSyncEventPublisher.PublishChatEventAsync("message_received")
   ↓
   BedrockChat.GenerateResponseAsync(chat.History)
   ↓
   Creates assistant message
   ↓
   Adds to chat.History
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
   Connected clients receive real-time update
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
     - Remove from dictionary
     - Dispose resources
   ```

## Load Representation Strategy

### The Problem

AWS App Runner scales instances based solely on **concurrent HTTP requests**. When HTTP requests complete, the request count drops to zero, even if background tasks are still running. This creates a critical issue for ChatModule:

1. Client sends `POST /chat/create` → Request completes immediately
2. Background task processes messages for 2-5 seconds (Bedrock API call)
3. App Runner sees: **0 concurrent requests** = Instance appears idle
4. App Runner may scale down and **terminate the instance**, killing active background tasks

### The Solution: Keep-Alive Long-Polling

ChatModule implements a **self-requesting keep-alive pattern** where each active chat maintains a corresponding long-polling HTTP request:

```
Active Chat Lifecycle:
1. POST /chat/create → Creates chat → Returns immediately
2. ChatManagerService creates SemaphoreSlim(0, 1) for this chat
3. ChatManagerService initiates POST /internal/keepalive/{chatId} (fire-and-forget)
4. Internal endpoint calls semaphore.WaitAsync() → BLOCKS REQUEST
5. Background task processes messages while keep-alive request is held open
6. Client closes chat → CloseChatAsync() releases semaphore
7. Keep-alive request completes → HTTP request count decrements

Result: 1 Active Chat = 1 Concurrent HTTP Request
```

### Implementation Details

**ChatManagerService**:
- `_keepAliveSemaphores`: `ConcurrentDictionary<string, SemaphoreSlim>`
- `CreateChatAsync()`: Creates semaphore (unreleased), starts keep-alive POST
- `InitiateKeepAliveAsync()`: POSTs to `http://localhost:8080/ChatModule/internal/keepalive/{chatId}`
- `CloseChatInternalAsync()`: Releases semaphore, completing keep-alive request
- `GetKeepAliveSemaphore()`: Exposes semaphore to internal endpoint

**InternalController** (`/ChatModule/internal/keepalive/{chatId}`):
- Retrieves semaphore for chatId
- Calls `await semaphore.WaitAsync(cancellationToken)`
- Blocks HTTP request until chat closes or request is cancelled
- Returns 200 OK when chat closes normally

**HttpClient Configuration**:
- Named client: "KeepAlive"
- Timeout: 60 minutes (longer than 30-min chat timeout)
- Registered in DI: `services.AddHttpClient("KeepAlive")`

### Load Representation

This pattern accurately represents instance load to App Runner:

| Active Chats | Keep-Alive Requests | User API Requests | Total Concurrent Requests |
|--------------|---------------------|-------------------|---------------------------|
| 0            | 0                   | 0                 | 0                         |
| 50           | 50                  | ~10-20            | ~60-70                    |
| 100          | 100                 | ~20-40            | ~120-140                  |

**MaxConcurrency Calculation**:
- Keep-alive requests: Up to 100 (one per active chat)
- User API requests: ~50 peak (create, send, status, messages, close)
- **Total MaxConcurrency: 150** (configured in App Runner)

### Benefits

✅ **Accurate Load Tracking**: Each background task = 1 concurrent request
✅ **Prevents Premature Scale-Down**: Instance won't terminate while processing
✅ **Natural Scaling Trigger**: More chats = higher request count = scale-up
✅ **No External Dependencies**: Self-contained, uses standard HTTP
✅ **Works with App Runner Model**: Legitimate use of request-based scaling

### Trade-offs

⚠️ **Connection Pool Usage**: Each keep-alive uses a connection (minimal overhead with HTTP/2)
⚠️ **MaxConcurrency Headroom**: Must account for keep-alive requests in capacity planning
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
  MaxConcurrency: 150  # Updated to account for keep-alive requests
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
const createResponse = await chatApi.chatModuleCreateChatAsync({
  initialMessage: "Hello!",
  chatMetadata: { source: "web" }
});

const chatId = createResponse.chat.chatId;

// Subscribe to real-time events
eventClient.subscribe(`chat/${chatId}`, (event) => {
  if (event.eventType === 'message_completed') {
    const message = event.data;
    console.log('AI Response:', message.content);
  }
});

// Send message
await chatApi.chatModuleSendMessageAsync(chatId, {
  content: "Tell me more about MagicPets"
});

// Get message history
const history = await chatApi.chatModuleGetChatMessagesAsync(
  chatId,
  1,  // page
  50  // limit
);

// Close chat when done
await chatApi.chatModuleCloseChatAsync(chatId);
```

### Client SDK (C# / Blazor)

```csharp
var chatApi = serviceProvider.GetRequiredService<IChatApi>();

// Create chat
var createResponse = await chatApi.ChatModuleCreateChatAsync(
    new CreateChatRequest
    {
        InitialMessage = "Hello!",
        ChatMetadata = new { source = "blazor" }
    });

var chatId = createResponse.Chat.ChatId;

// Send message
var sendResponse = await chatApi.ChatModuleSendMessageAsync(
    chatId,
    new SendMessageRequest { Content = "Tell me more" });

// Get status
var status = await chatApi.ChatModuleGetChatStatusAsync(chatId);
Console.WriteLine($"Chat has {status.MessageCount} messages");

// Get messages
var messages = await chatApi.ChatModuleGetChatMessagesAsync(
    chatId,
    page: 1,
    limit: 50);

// Close
await chatApi.ChatModuleCloseChatAsync(chatId);
```

## Limitations and Considerations

### In-Memory State

- **Not Persistent**: Chat state is lost if container restarts
- **Not Shared**: Each App Runner instance has independent state
- **Session Affinity Required**: Clients must route to same instance (use sticky sessions)
- **Scalability**: Memory usage grows with active chats (mitigated by 30-min cleanup)

### Keep-Alive Load Representation

- **Solved**: Keep-alive long-polling prevents premature scale-down (see "Load Representation Strategy" section)
- **MaxConcurrency**: Set to 150 to account for ~100 keep-alive requests + ~50 user API requests
- **Connection Overhead**: Each active chat maintains one HTTP connection (minimal with HTTP/2)
- **Timeout Management**: Keep-alive requests timeout at 60 minutes, longer than 30-min chat timeout

### Recommendations

For production deployments requiring persistence:
- Consider DynamoDB for chat/message storage
- Implement session rehydration on container startup
- Use ElastiCache/Redis for distributed session state
- Add database write-through cache pattern

### Performance

- **Concurrent Chats**: Supports 100+ concurrent chats per instance
- **Message Latency**: ~2-5 seconds for Bedrock response
- **Auto-scaling**: Scales based on CPU/memory (not chat count)

## Future Enhancements

- [ ] **Persistent storage**: Chat and ChatMessage entities already implement `IItem` interface, ready for DynamoDB persistence via `ChatRepo` base methods
- [ ] **Streaming responses**: Real-time token delivery via AppSync Events
- [ ] **Chat history search**: Full-text search across message content
- [ ] **Multi-model support**: GPT-4, Claude Opus, Gemini, etc.
- [ ] **Rate limiting**: Per-user throttling and quotas
- [ ] **Analytics**: Usage metrics, conversation analytics, user insights
- [ ] **Context window management**: Intelligent truncation and summarization
- [ ] **Conversation summarization**: Auto-summarize long conversations

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
