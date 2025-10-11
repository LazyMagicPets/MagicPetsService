# Recommendation: Refactor AppSyncEventPublisher for Single Events API Per Container

## Current Problems

### 1. Hard-Coded Dual Configuration
The `AppSyncEventPublisher` currently reads configuration for **both** TenantEventsApi and ConsumerEventsApi:

```csharp
// Lines 19-22 in AppSyncEventPublisher.cs
private readonly string? _tenantEventApiHttpDomain;
private readonly string? _consumerEventApiHttpDomain;
private readonly string? _tenantEventApiKey;
private readonly string? _consumerEventApiKey;

// Lines 40-43
_tenantEventApiHttpDomain = _configuration["AWS:AppSync:TenantEventsApi:HttpDomain"];
_consumerEventApiHttpDomain = _configuration["AWS:AppSync:ConsumerEventsApi:HttpDomain"];
_tenantEventApiKey = _configuration["AWS:AppSync:TenantEventsApi:ApiKey"];
_consumerEventApiKey = _configuration["AWS:AppSync:ConsumerEventsApi:ApiKey"];

// Line 83 - Always uses Tenant
var eventApiHttpDomain = _tenantEventApiHttpDomain;

// Line 129 - Always uses Tenant
var apiKey = _tenantEventApiKey;
```

**Issues:**
- Reads configuration for both APIs but only uses one
- Hard-coded to always use TenantEventsApi (line 83, 129)
- ConsumerEventsApi configuration is loaded but never used
- No way to switch between APIs based on container type

### 2. Mismatch Between CloudFormation and Code

**CloudFormation template** (sam.service.apprunner.yaml line 27-28):
```yaml
- Name: AWS__AppSync__EventApiId
  Value: !Ref __AppSyncEventsApiName__
```

**Code expects** (AppSyncEventPublisher.cs lines 40-43):
```csharp
_configuration["AWS:AppSync:TenantEventsApi:HttpDomain"]
_configuration["AWS:AppSync:TenantEventsApi:ApiKey"]
```

**Problem:** The container receives `EventApiId` but code expects `HttpDomain` and `ApiKey`.

### 3. LocalWebService vs AppRunner Configuration Gap

**LocalWebService** (launchSettings.json):
```json
{
  "APPSYNC_EVENTS_API_TYPE": "Tenant"  // Hint for which API to use
}
```

**AppRunner containers**: Only get `EventApiId` - no HttpDomain or ApiKey values

**Problem:** No mechanism to resolve HttpDomain and ApiKey from EventApiId in AppRunner containers.

### 4. Architecture Reality

Each AppRunner container instance is dedicated to either:
- **TenantApi** container → Uses TenantEventsApi only
- **ConsumerApi** container → Uses ConsumerEventsApi only

**Never both**. The current dual-configuration approach violates this reality.

## Recommended Solution

### Strategy: Single Events API Configuration per Container

Each container should be configured with **one** Events API - the one it actually uses.

### Changes Required

#### 1. Update CloudFormation Template

**File:** `Service/AWSTemplates/Snippets/sam.service.apprunner.yaml`

**Before:**
```yaml
RuntimeEnvironmentVariables:
  - Name: AWS__AppSync__EventApiId
    Value: !Ref __AppSyncEventsApiName__
```

**After:**
```yaml
RuntimeEnvironmentVariables:
  - Name: AWS__AppSync__EventsApi__HttpDomain
    Value: !GetAtt __AppSyncEventsApiName__.Dns.Http
  - Name: AWS__AppSync__EventsApi__ApiKey
    Value: !GetAtt __AppSyncEventsApiName__ApiKey.ApiKey
  - Name: AWS__AppSync__EventsApi__Region
    Value: !Ref AWS::Region
```

**Benefits:**
- Direct values instead of references requiring AWS API calls
- Container gets all needed information at startup
- No conditional logic needed in code

#### 2. Refactor AppSyncEventPublisher

**File:** `Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs`

**Before:**
```csharp
private readonly string? _tenantEventApiHttpDomain;
private readonly string? _consumerEventApiHttpDomain;
private readonly string? _tenantEventApiKey;
private readonly string? _consumerEventApiKey;

_tenantEventApiHttpDomain = _configuration["AWS:AppSync:TenantEventsApi:HttpDomain"];
_consumerEventApiHttpDomain = _configuration["AWS:AppSync:ConsumerEventsApi:HttpDomain"];
_tenantEventApiKey = _configuration["AWS:AppSync:TenantEventsApi:ApiKey"];
_consumerEventApiKey = _configuration["AWS:AppSync:ConsumerEventsApi:ApiKey"];

var eventApiHttpDomain = _tenantEventApiHttpDomain;
var apiKey = _tenantEventApiKey;
```

**After:**
```csharp
private readonly string? _eventApiHttpDomain;
private readonly string? _eventApiKey;

// Single source of truth - works for both AppRunner and LocalWebService
_eventApiHttpDomain = _configuration["AWS:AppSync:EventsApi:HttpDomain"];
_eventApiKey = _configuration["AWS:AppSync:EventsApi:ApiKey"];
_region = _configuration["AWS:AppSync:EventsApi:Region"]
    ?? _configuration["AWS_REGION"]
    ?? _configuration["AWS:Region"]
    ?? "us-east-1";

// Direct usage - no conditional logic
var eventApiHttpDomain = _eventApiHttpDomain;
var apiKey = _eventApiKey;
```

**Benefits:**
- Single configuration path
- Works identically in AppRunner and LocalWebService
- No unused configuration loaded
- Clear and simple

#### 3. Update LocalWebService Configuration

**Option A: Use environment variables** (Recommended for consistency with AppRunner)

**File:** `Service/LocalWebService/Properties/launchSettings.json`

**Before:**
```json
{
  "environmentVariables": {
    "ASPNETCORE_ENVIRONMENT": "Development",
    "APPSYNC_EVENTS_API_TYPE": "Tenant"
  }
}
```

**After:**
```json
{
  "environmentVariables": {
    "ASPNETCORE_ENVIRONMENT": "Development",
    "AWS__AppSync__EventsApi__HttpDomain": "24njcduygfd35m34apqhaaqs6e.appsync-api.us-west-2.amazonaws.com",
    "AWS__AppSync__EventsApi__ApiKey": "da2-xxx...",
    "AWS__AppSync__EventsApi__Region": "us-west-2"
  }
}
```

**Option B: Use appsettings.json** (Better for multi-developer environments)

**File:** `Service/LocalWebService/appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AWS": {
    "AppSync": {
      "EventsApi": {
        "HttpDomain": "24njcduygfd35m34apqhaaqs6e.appsync-api.us-west-2.amazonaws.com",
        "ApiKey": "da2-xxx...",
        "Region": "us-west-2"
      }
    }
  }
}
```

**Benefits:**
- Can be added to .gitignore for secrets
- Easy to switch between local and cloud APIs
- Consistent with AppRunner configuration

**Option C: Hybrid - User Secrets** (Best for development)

Use .NET User Secrets for API keys:

```bash
dotnet user-secrets set "AWS:AppSync:EventsApi:HttpDomain" "24njcduygfd35m34apqhaaqs6e.appsync-api.us-west-2.amazonaws.com"
dotnet user-secrets set "AWS:AppSync:EventsApi:ApiKey" "da2-xxx..."
dotnet user-secrets set "AWS:AppSync:EventsApi:Region" "us-west-2"
```

#### 4. Configuration Loading Order

The updated code will support multiple configuration sources in priority order:

```csharp
// 1. Check explicit EventsApi configuration (AppRunner containers, appsettings, user secrets)
_eventApiHttpDomain = _configuration["AWS:AppSync:EventsApi:HttpDomain"];
_eventApiKey = _configuration["AWS:AppSync:EventsApi:ApiKey"];

// 2. Fallback for legacy configuration (optional - for migration period)
if (string.IsNullOrEmpty(_eventApiHttpDomain))
{
    var apiType = _configuration["APPSYNC_EVENTS_API_TYPE"] ?? "Tenant";
    _eventApiHttpDomain = _configuration[$"AWS:AppSync:{apiType}EventsApi:HttpDomain"];
    _eventApiKey = _configuration[$"AWS:AppSync:{apiType}EventsApi:ApiKey"];
}

// 3. Region resolution (multiple sources)
_region = _configuration["AWS:AppSync:EventsApi:Region"]
    ?? _configuration["AWS_REGION"]
    ?? _configuration["AWS:Region"]
    ?? "us-east-1";
```

### Implementation Plan

#### Phase 1: Update AppSyncEventPublisher (Breaking Changes)

1. Remove dual configuration fields
2. Add single configuration fields
3. Update constructor to read from new configuration paths
4. Remove conditional logic that always chose Tenant
5. Update logging to reflect single API usage

#### Phase 2: Update CloudFormation Template

1. Update `sam.service.apprunner.yaml` snippet
2. Regenerate CloudFormation template
3. Update both TenantApi and ConsumerApi AppRunner services

#### Phase 3: Update LocalWebService

1. Choose configuration approach (appsettings.json vs user secrets)
2. Update configuration files
3. Test with local development

#### Phase 4: Migration & Deployment

1. Deploy updated CloudFormation stack (updates environment variables)
2. Deploy updated container images
3. Verify events are publishing correctly
4. Remove legacy configuration code after verification period

### Code Changes Detail

**Full AppSyncEventPublisher.cs Constructor:**

```csharp
public AppSyncEventPublisher(
    AwsCredentialsCache credentialsCache,
    ILogger<AppSyncEventPublisher> logger,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory)
{
    _credentialsCache = credentialsCache;
    _logger = logger;
    _configuration = configuration;
    _httpClientFactory = httpClientFactory;

    // Single Events API configuration per container
    // AppRunner: Reads from environment variables set by CloudFormation
    // LocalWebService: Reads from appsettings.json or user secrets
    _eventApiHttpDomain = _configuration["AWS:AppSync:EventsApi:HttpDomain"];
    _eventApiKey = _configuration["AWS:AppSync:EventsApi:ApiKey"];

    // Region resolution with multiple fallbacks
    _region = _configuration["AWS:AppSync:EventsApi:Region"]
        ?? _configuration["AWS_REGION"]
        ?? _configuration["AWS:Region"]
        ?? "us-east-1";

    // Validation
    if (string.IsNullOrEmpty(_eventApiHttpDomain))
    {
        _logger.LogWarning("AppSync Events API HttpDomain not configured. Events will not be published.");
        _logger.LogWarning("Expected configuration key: AWS:AppSync:EventsApi:HttpDomain");
    }

    if (string.IsNullOrEmpty(_eventApiKey) && UseApiKeyAuth)
    {
        _logger.LogWarning("AppSync Events API Key not configured. Events may fail to publish.");
        _logger.LogWarning("Expected configuration key: AWS:AppSync:EventsApi:ApiKey");
    }

    _logger.LogInformation("AppSync Events Publisher initialized with domain: {Domain}",
        _eventApiHttpDomain ?? "NOT CONFIGURED");
}
```

**Full PublishChatEventAsync Update:**

```csharp
public async Task PublishChatEventAsync(string chatId, ChatEvent sessionEvent)
{
    try
    {
        // ... [data type extraction code remains same] ...

        // Single Events API - no conditional logic
        if (string.IsNullOrEmpty(_eventApiHttpDomain))
        {
            _logger.LogWarning("AppSync Event API HTTP Domain not configured, logging event instead");
            _logger.LogDebug("Event payload: {Payload}", JsonSerializer.Serialize(eventPayload));
            return;
        }

        // Publish to the configured Events API
        await PublishEventAsync(_eventApiHttpDomain, chatId, eventPayload, sessionEvent.EventType.ToString());
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish session event: {EventType} for session: {SessionId}",
            sessionEvent.EventType, chatId);
        // Don't throw - event publishing should be non-blocking
    }
}
```

**Full PublishEventAsync Update:**

```csharp
private async Task PublishEventAsync(string httpDomain, string chatId, object eventPayload, string eventType)
{
    // ... [channel and body construction remains same] ...

    if (UseApiKeyAuth)
    {
        httpClient.DefaultRequestHeaders.Clear();

        // Use the configured API key (no conditional logic)
        if (!string.IsNullOrEmpty(_eventApiKey))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", _eventApiKey);
            _logger.LogDebug("Using API Key authentication");
        }
        else
        {
            _logger.LogWarning("API Key not configured for AppSync Events API");
        }

        content.Headers.Remove("Content-Type");
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
    }
    else
    {
        // ... [IAM/SigV4 code remains same] ...
    }

    // ... [rest of method remains same] ...
}
```

## Benefits of This Approach

### 1. **Architectural Clarity**
- Each container has exactly the configuration it needs
- No unused configuration loaded
- Code matches deployment reality

### 2. **Maintainability**
- Single configuration path to understand and debug
- No conditional logic based on API type
- Easy to test locally with any Events API

### 3. **Reusability**
- `AppSyncEventPublisher` works with **any** Events API
- Can easily add more Events APIs in future (e.g., PublicEventsApi)
- Just deploy new container with different configuration

### 4. **Configuration Consistency**
- AppRunner and LocalWebService use same configuration keys
- Environment variables map directly to appsettings.json structure
- User secrets work seamlessly in development

### 5. **Deployment Simplicity**
- CloudFormation provides complete configuration
- No runtime AWS API calls needed to resolve EventApiId
- Container is self-contained and ready to publish on startup

### 6. **Security**
- API keys stay in CloudFormation secrets/parameters
- LocalWebService can use .NET User Secrets (not checked into git)
- No secrets hard-coded in configuration files

## Migration Path for Existing Deployments

### Step 1: Update Code (Backward Compatible)

Add new configuration reading with fallback:

```csharp
// Try new configuration first
_eventApiHttpDomain = _configuration["AWS:AppSync:EventsApi:HttpDomain"];
_eventApiKey = _configuration["AWS:AppSync:EventsApi:ApiKey"];

// Fallback to old configuration (temporary - for migration)
if (string.IsNullOrEmpty(_eventApiHttpDomain))
{
    _eventApiHttpDomain = _configuration["AWS:AppSync:TenantEventsApi:HttpDomain"];
    _eventApiKey = _configuration["AWS:AppSync:TenantEventsApi:ApiKey"];
    _logger.LogWarning("Using legacy TenantEventsApi configuration. Please update to EventsApi configuration.");
}
```

### Step 2: Update CloudFormation

Deploy CloudFormation changes that add new environment variables while keeping old ones:

```yaml
- Name: AWS__AppSync__EventsApi__HttpDomain  # NEW
  Value: !GetAtt __AppSyncEventsApiName__.Dns.Http
- Name: AWS__AppSync__EventsApi__ApiKey  # NEW
  Value: !GetAtt __AppSyncEventsApiName__ApiKey.ApiKey
- Name: AWS__AppSync__EventApiId  # OLD - can be removed later
  Value: !Ref __AppSyncEventsApiName__
```

### Step 3: Deploy Updated Containers

Deploy new container images with backward-compatible code.

### Step 4: Verify & Remove Fallback

After verification period:
1. Remove fallback code
2. Remove old CloudFormation environment variables
3. Deploy final clean version

## Alternative Consideration: AWS SDK Integration

**Alternative approach** (NOT recommended): Use AWS SDK to resolve HttpDomain and ApiKey from EventApiId at runtime.

**Why NOT recommended:**
- Adds AWS SDK dependency
- Requires AWS API call on every app startup
- Requires additional IAM permissions
- Slower startup time
- More complex error handling
- Not necessary when CloudFormation can provide values directly

**When it WOULD make sense:**
- If EventApiId changed dynamically (it doesn't)
- If multi-region failover needed (not current requirement)
- If API keys rotated frequently (they don't expire in current setup)

## Conclusion

The current `AppSyncEventPublisher` implementation has hard-coded dual configuration that:
1. Doesn't match the single-Events-API-per-container reality
2. Has mismatch between CloudFormation output and code expectations
3. Is not reusable for different container types

**Recommended solution:**
- Update CloudFormation to pass HttpDomain and ApiKey directly
- Refactor code to use single configuration path
- Remove conditional logic and unused configuration
- Maintain consistency between AppRunner and LocalWebService

This approach is:
- ✅ Simple and maintainable
- ✅ Matches deployment reality
- ✅ Reusable for any Events API
- ✅ Easy to configure locally
- ✅ No runtime AWS API calls needed

---

**Next Steps:**
1. Review and approve this recommendation
2. Implement Phase 1 (code changes) with backward compatibility
3. Test locally with updated configuration
4. Implement Phase 2 (CloudFormation changes)
5. Deploy and verify
6. Remove fallback code after verification period

---

## Implementation Status

✅ **IMPLEMENTED AND EXTENDED** - 2025-01-11

This recommendation has been successfully implemented and exceeded with additional improvements:

### What Was Implemented

#### Phase 1: Configuration Refactoring (From This Recommendation)
1. ✅ AppSyncEventPublisher refactored with unified configuration
2. ✅ Single `EventsApi` configuration path with backward-compatible fallback
3. ✅ CloudFormation template updated to pass direct values via !GetAtt
4. ✅ LocalWebService Startup.g.cs updated to set unified configuration
5. ✅ Build and testing succeeded

**Result**: Eliminated hard-coded dual configuration, matched deployment reality, simplified code

#### Phase 2: Additional Abstraction Layer (Beyond This Recommendation)
After completing the configuration refactoring, a more comprehensive abstraction was implemented:

1. ✅ **Two-Layer Architecture**:
   - Domain Layer: `IChatEventPublisher` - Business-focused event methods
   - Transport Layer: `IWsEventPublisher` - Platform-agnostic WebSocket interface

2. ✅ **Platform Independence**:
   - Renamed from AWS-specific `IAppSyncEventPublisher` to generic `IWsEventPublisher`
   - Enables future implementations: SignalR, Azure Event Grid, etc.
   - Clean separation of domain logic and transport implementation

3. ✅ **Simplified Business Logic**:
   - ChatManagerService event publishing reduced by 83% (6 lines → 1 line per event)
   - High-level domain methods hide transport complexity
   - Type-safe, intention-revealing API

4. ✅ **Implementation Classes**:
   - `ChatEventPublisher` - Domain implementation
   - `AppSyncWsEventPublisher` - AWS AppSync transport implementation
   - `MockWsEventPublisher` - Test mock for transport layer

### Files Modified

**Configuration Refactoring:**
1. `Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs` (later replaced)
2. `Service/ProjectTemplates/WebApi/Startup.g.cs`
3. `Service/AWSTemplates/Snippets/sam.service.apprunner.yaml`

**Abstraction Layer:**
1. Created: `IChatEventPublisher.cs`, `ChatEventPublisher.cs`
2. Created: `IWsEventPublisher.cs`, `AppSyncWsEventPublisher.cs`
3. Created: `MockWsEventPublisher.cs`
4. Modified: `ChatManagerService.cs`, `ServiceRepoExtensions.cs`
5. Modified: `ChatModuleTestFixture.cs`, `ChatModuleTests.cs`
6. Deleted: Old `IAppSyncEventPublisher.cs`, `AppSyncEventPublisher.cs`, `MockAppSyncEventPublisher.cs`

### Benefits Achieved

**From Original Recommendation:**
- ✅ Single configuration path per container
- ✅ No unused configuration loaded
- ✅ Code matches deployment reality
- ✅ Maintainability improved
- ✅ Reusable for any Events API
- ✅ Configuration consistency across environments

**Additional Benefits from Abstraction Layer:**
- ✅ Platform-independent architecture
- ✅ Dramatically simplified business logic (83% code reduction)
- ✅ Clean separation of concerns
- ✅ Easy to add new transport implementations
- ✅ Better testability at both layers
- ✅ Intention-revealing domain API

### Related Documents

- `IMPLEMENTATION_PLAN_AppSyncEventPublisher.md` - Detailed plan for configuration refactoring
- `IMPLEMENTATION_COMPLETE.md` - Summary of configuration refactoring completion
- `PLAN_EventPublisher_Abstraction.md` - Plan for abstraction layer
- `ABSTRACTION_COMPLETE.md` - Detailed summary of abstraction implementation
- `ChatEvents.md` - Updated documentation with new two-layer architecture

### Conclusion

This recommendation successfully addressed the original problems and enabled a more comprehensive architectural improvement. The final implementation:

1. **Solved all identified problems**: Hard-coded dual config, CloudFormation mismatch, LocalWebService/AppRunner gap
2. **Exceeded the recommendation**: Added platform-independent abstraction layer
3. **Simplified the codebase**: 83% reduction in event publishing code
4. **Future-proofed the design**: Easy to support multiple platforms and transports

The recommendation's core insight - "Each container should be configured with one Events API" - proved correct and laid the foundation for the broader abstraction work that followed.
