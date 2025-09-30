# App Runner and AppSync Events Integration

## Overview
This document describes the implementation of AWS App Runner and AppSync Events into the MagicPetsService to enable concurrent processing of long-running LLM orchestration tasks with real-time client updates.

## Architecture Goals
- Support concurrent request processing (unlike Lambda's single-request model)
- Enable long-running LLM and MCP service orchestration
- Provide real-time updates to clients via AppSync Events (not GraphQL)
- Maintain session affinity for stateful conversations
- Use in-memory session state for simplicity and performance

## Core Concepts

### Connection Sessions
- Each client request creates a "connection session" with a unique ID
- Sessions spawn background tasks that process LLM/MCP calls asynchronously
- All session state is maintained in-memory (no persistent storage)
- Sessions terminate when the connection is closed or times out

### App Runner Stateless Nature
**Important:** App Runner is stateless and instances can be replaced at any time. This means:
- Sessions are ephemeral and tied to instance lifetime
- Instance replacement = session loss (by design)
- No guarantees of session persistence
- Clients must handle session loss gracefully
- This is acceptable for conversational AI where sessions are naturally transient

### Request Flow
1. Client sends initial request to App Runner endpoint
2. App Runner creates connection session and starts background task
3. Immediate response returns session ID to client
4. Client subscribes to AppSync Events channel using session ID (not GraphQL)
5. Background task processes LLM/MCP calls and publishes events via AppSync Events
6. Client can send additional messages using session ID
7. Routing attempts to send requests to the same instance (best effort)

## Implementation Todo List

### Quick Progress
- [ ] Phase 0: LazyMagicMDD Updates
- [ ] Phase 1: Infrastructure Setup
- [ ] Phase 2: API Design
- [ ] Phase 3: Container Implementation
- [ ] Phase 4: Session Management
- [ ] Phase 5: AppSync Events Integration
- [ ] Phase 6: Client Integration

### Phase 0: LazyMagicMDD Generator Updates
**Location:** `C:\Users\TimothyMay\repos\_Dev\LazyMagic\LazyMagicMDD`

- [ ] Create AwsAppRunnerResource artifact generator
- [ ] Create AwsAppSyncEventsResource artifact generator
- [ ] Add App Runner YAML template snippets
- [ ] Add AppSync Events YAML template snippets
- [ ] Create DotNetAppRunnerProject artifact generator
- [ ] Add Dockerfile generation for App Runner containers

**New Artifact Generators Required:**

#### ApiArtifacts (App Runner and AppSync Events Support)
- `AwsAppRunnerResource.cs` - Generates App Runner service CloudFormation resource (API-level)
- `AwsAppSyncEventsResource.cs` - Generates AppSync Events API CloudFormation resource (not GraphQL)
- `DotNetAppSyncEventsSDKProject.cs` - Generates client SDK wrapper for AppSync Events (not GraphQL)
- Template: `AWSTemplates/Snippets/sam.service.apprunner.yaml`
- Template: `AWSTemplates/Snippets/sam.service.appsync-events.yaml`

#### ContainerArtifacts (App Runner Container Support)
- `DotNetAppRunnerProject.cs` - Generates .NET project structure for App Runner containers
- Template: Dockerfile and project structure templates

**Pattern:** Follow existing `AwsApiLambdaResource` and `AwsHttpApiResource` implementations

### Phase 1: Infrastructure Setup
- [ ] Add AppRunnerContainerDefault directive to LazyMagic.yaml
- [ ] Add ApiAppRunnerDefault directive to LazyMagic.yaml
- [ ] Add ApiAppSyncEventsDefault directive to LazyMagic.yaml
- [ ] Define ChatAppRunner container configuration using new artifacts
- [ ] Create ChatApi configuration using ApiAppRunnerDefault
- [ ] Create EventsApi configuration using ApiAppSyncEventsDefault
- [ ] Test artifact generation with LazyMagicMDD

### Phase 2: API Design
- [ ] Create openapi.chat.yaml with session endpoints
- [ ] Define schemas in openapi.chat-schema.yaml
- [ ] Extend messaging schemas for AppSync Events
- [ ] Update client SDK generation configuration

### Phase 3: Container Implementation
- [ ] Create ChatAppRunner project structure
- [ ] Implement Cognito JWT validation middleware
- [ ] Add authentication/authorization logic
- [ ] Implement SessionManager with in-memory state
- [ ] Add background task processing logic
- [ ] Integrate Bedrock LLM client
- [ ] Implement MCP service orchestration
- [ ] Add AppSync Events publisher

### Phase 4: Session Management
- [ ] Implement session creation and lifecycle
- [ ] Add message queueing with channels
- [ ] Handle session timeout and cleanup
- [ ] Implement graceful shutdown handling

### Phase 5: AppSync Events Integration
- [ ] Create AppSync Events CloudFormation template (not GraphQL)
- [ ] Define event channels for session-based messaging
- [ ] Implement authentication/authorization for AppSync Events
- [ ] Add event publishing from background tasks using AWS AppSync Events SDK
- [ ] Create session subscription management wrapper

### Phase 6: Client Integration
- [ ] Update client SDKs for session management
- [ ] Add AppSync Events support (not GraphQL WebSocket)
- [ ] Implement reconnection logic using AWS AppSync Events SDK
- [ ] Add event handling in client applications

### Current Task
**Working on:** [Update as you progress]
**Blockers:** None
**Notes:**

## Technical Design

### Session Manager Architecture
```csharp
public class SessionManager
{
    private readonly ConcurrentDictionary<string, ConnectionSession> _sessions;
    private readonly ConcurrentDictionary<string, Task> _backgroundTasks;

    public Task<string> CreateSession(CreateSessionRequest request);
    public Task SendMessage(string sessionId, SessionMessage message);
    public Task CloseSession(string sessionId);
}
```

### Connection Session Model
```csharp
public class ConnectionSession
{
    public string SessionId { get; }
    public DateTime CreatedAt { get; }
    public Channel<SessionMessage> MessageQueue { get; }
    public CancellationTokenSource CancellationToken { get; }
    public Dictionary<string, object> Context { get; }
    public List<LLMResponse> History { get; }
}
```

### API Endpoints

#### Chat API
- `POST /session/create` - Create new connection session
- `POST /session/{sessionId}/message` - Send message to existing session
- `GET /session/{sessionId}/status` - Get session status
- `DELETE /session/{sessionId}` - Close session

### App Runner Configuration
```yaml
AutoScalingConfiguration:
  MaxConcurrency: 100  # Concurrent requests per instance
  MaxSize: 10          # Maximum instances
  MinSize: 1           # Minimum instances

HealthCheckConfiguration:
  Path: /health
  Interval: 10
  Timeout: 5
  HealthyThreshold: 2
  UnhealthyThreshold: 3
```

### Session Routing (Not Sticky Sessions)
- App Runner is stateless - instances can be replaced at any time
- We implement application-level routing using session IDs
- If an instance is replaced, the session is lost (acceptable for our use case)
- No persistent state store - all session data is ephemeral
- Session affinity is "best effort" through consistent hashing or similar routing

### Authentication Implementation

Since App Runner doesn't provide built-in Cognito authentication (unlike API Gateway), we must implement JWT validation within the application:

#### Cognito JWT Validation Middleware
```csharp
public class CognitoAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private JsonWebKeySet _jwks;

    public async Task InvokeAsync(HttpContext context)
    {
        var token = ExtractTokenFromHeader(context);

        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            return;
        }

        var claimsPrincipal = await ValidateToken(token);
        if (claimsPrincipal == null)
        {
            context.Response.StatusCode = 401;
            return;
        }

        context.User = claimsPrincipal;
        await _next(context);
    }

    private async Task<ClaimsPrincipal> ValidateToken(string token)
    {
        // Download JWKS from Cognito if not cached
        // Validate JWT signature
        // Verify token claims (exp, iss, aud, etc.)
        // Return ClaimsPrincipal if valid
    }
}
```

#### Authentication Flow
1. Client obtains JWT from Cognito User Pool
2. Client includes JWT in Authorization header
3. App Runner middleware validates JWT
4. Extract user context and permissions
5. Associate user with connection session

## File Structure
```
Service/
├── AppRunner.md (this file)
├── LazyMagic.yaml
├── openapi.chat.yaml
├── openapi.chat-schema.yaml
├── Containers/
│   └── ChatAppRunner/
│       ├── ChatAppRunner.csproj
│       ├── Program.cs
│       ├── Dockerfile
│       ├── Services/
│       │   ├── SessionManager.cs
│       │   ├── BedrockChat.cs
│       │   ├── MCPServiceClient.cs
│       │   └── AppSyncEventPublisher.cs
│       └── Models/
│           ├── ConnectionSession.cs
│           └── SessionMessage.cs
├── Modules/
│   └── ChatModule/
│       ├── ChatModule.csproj
│       └── Controllers/
│           └── SessionController.cs
└── AWSTemplates/
    └── Snippets/
        ├── sam.service.apprunner.yaml
        └── sam.service.appsync-events.yaml
```

## Implementation Notes

### Memory Management
- Sessions are entirely in-memory
- Automatic cleanup on connection close
- Consider memory limits when setting max concurrency
- Implement session timeout (e.g., 30 minutes)

### Error Handling
- Graceful degradation if session not found
- Automatic retry for transient failures
- Dead letter queue for failed messages
- Circuit breaker for external service calls

### Monitoring
- CloudWatch metrics for session count
- Custom metrics for LLM latency
- AppSync Events delivery metrics
- App Runner scaling metrics

### Security
- Cognito JWT validation for all requests
- Session IDs should be cryptographically random
- Validate session ownership against authenticated user
- Rate limiting per session and per user
- Input sanitization for LLM prompts
- JWKS caching with periodic refresh
- Token expiration handling

## Testing Strategy

### Unit Tests
- SessionManager operations
- Message queueing logic
- Event publishing
- Session cleanup

### Integration Tests
- End-to-end session flow
- AppSync Events delivery
- LLM integration
- MCP service orchestration

### Load Tests
- Concurrent session handling
- Memory usage under load
- Scaling behavior
- Session affinity verification

## Deployment Strategy

1. Deploy infrastructure changes (SAM templates)
2. Deploy App Runner service with minimal instances
3. Deploy AppSync Events API
4. Update client SDKs
5. Gradual rollout with feature flags
6. Monitor metrics and logs
7. Scale based on usage patterns

## Future Enhancements

- Session persistence for recovery
- Multi-region deployment
- Advanced routing strategies
- WebRTC for audio/video streams
- Session recording and replay

## LazyMagicMDD Integration

### Generator Location
**Path:** `C:\Users\TimothyMay\repos\_Dev\LazyMagic\LazyMagicMDD`

### Required Artifact Generators

Following the LazyMagicMDD pattern, we need to create new artifact generators:

#### AwsAppRunnerResource (ApiArtifacts)
Generates AWS App Runner service CloudFormation resource, similar to `AwsHttpApiResource`:
```csharp
public class AwsAppRunnerResource : ArtifactBase, IAwsApiResource
{
    public override string Template { get; set; } = "AWSTemplates/Snippets/sam.service.apprunner.yaml";
    public string ExportedAwsResourceDefinition { get; set; } = "";
    public string ExportedAwsResourceName { get; set; } = "";
    public int Cpu { get; set; } = 1024;        // 1 vCPU
    public int Memory { get; set; } = 2048;     // 2 GB
    public int Port { get; set; } = 8080;       // Container port

    public override async Task GenerateAsync(SolutionBase solution, DirectiveBase directiveArg)
    {
        // Follow AwsHttpApiResource pattern
        // Process Api directive (not Container)
        // Connect to DotNetAppRunnerProject containers
        // Replace __tokens__ in template
        // Export resource definition
    }
}
```

#### DotNetAppRunnerProject (ContainerArtifacts)
Generates .NET project structure for App Runner, similar to `DotNetApiLambdaProject`:
```csharp
public class DotNetAppRunnerProject : DotNetProjectBase
{
    // Project generation for App Runner containers
    // Include Dockerfile generation
    // Configure for containerized deployment
}
```

#### AwsAppSyncEventsResource (ApiArtifacts)
Generates AppSync Events API CloudFormation resource, similar to `AwsHttpApiResource`:
```csharp
public class AwsAppSyncEventsResource : ArtifactBase, IAwsApiResource
{
    public override string Template { get; set; } = "AWSTemplates/Snippets/sam.service.appsync-events.yaml";
    // Follow AwsHttpApiResource pattern for event channel generation
}
```

#### DotNetAppSyncEventsSDKProject (ApiArtifacts)
Generates client SDK wrapper for AWS AppSync Events (not GraphQL), separate from HTTP REST SDKs:
```csharp
public class DotNetAppSyncEventsSDKProject : DotNetProjectBase
{
    // Generate wrapper around AWS AppSync Events SDK (not GraphQL)
    // Session-specific helper methods
    // Event serialization/deserialization
    // Channel naming conventions
    // Uses Amazon.AppSyncEvents NuGet package internally
    // Different from DotNetHttpApiSDKProject (REST)
}
```

### Container Configuration Pattern
Use existing Container directive structure with new artifact generators:

```yaml
# New default directive for App Runner containers
AppRunnerContainerDefault:
  Type: Container
  Artifacts:
    DotNetAppRunnerProject:      # New artifact generator
      # App Runner project configuration

# Container using App Runner artifacts
ChatAppRunner:
  Type: Container
  Defaults: AppRunnerContainerDefault
  Modules:
  - ChatModule
```

### Default Directives to Add
Need to add new default directives to LazyMagic.yaml:

```yaml
# Container Default for App Runner
AppRunnerContainerDefault:
  Type: Container
  Artifacts:
    DotNetAppRunnerProject:      # New artifact generator
    # No AwsApiLambdaResource (App Runner doesn't use Lambda)

# API Default for App Runner (NOT using ApiDefault)
ApiAppRunnerDefault:
  Type: Api
  IsDefault: true
  Artifacts:
    AwsAppRunnerResource:        # New artifact generator (instead of AwsHttpApiResource)
    DotNetHttpApiSDKProject:     # Reuse existing HTTP REST client SDK generator

# API Default for AppSync Events (NOT using ApiDefault)
ApiAppSyncEventsDefault:
  Type: Api
  IsDefault: true
  Artifacts:
    AwsAppSyncEventsResource:       # AppSync Events CloudFormation resource
    DotNetAppSyncEventsSDKProject:  # NEW: WebSocket client SDK (different from HTTP REST)
```

### API Configuration Pattern
Use existing Api directive structure with new default:
```yaml
# API for App Runner service (uses ApiAppRunnerDefault, not ApiDefault)
ChatApi:
  Type: Api
  Defaults: ApiAppRunnerDefault  # NEW: Cannot use ApiDefault (has AwsHttpApiResource)
  Containers:
  - ChatAppRunner

# API for AppSync Events (needs separate default for different SDK)
EventsApi:
  Type: Api
  Defaults: ApiAppSyncEventsDefault  # NEW: Different SDK than HTTP REST
  Artifacts:
    AwsAppSyncEventsResource:        # CloudFormation resource
    DotNetAppSyncEventsSDKProject:   # WebSocket client SDK (not HTTP REST)
```

## References

- [AWS App Runner Documentation](https://docs.aws.amazon.com/apprunner/)
- [AWS AppSync Events](https://docs.aws.amazon.com/appsync/latest/devguide/events.html)
- [Amazon Bedrock Integration](https://docs.aws.amazon.com/bedrock/)
- [Model Context Protocol (MCP)](https://modelcontextprotocol.io/)
- [LazyMagicMDD Generator](file:///C:/Users/TimothyMay/repos/_Dev/LazyMagic/LazyMagicMDD)

## Change Log

| Date | Author | Description |
|------|--------|-------------|
| 2024-09-30 | Initial | Created design document |