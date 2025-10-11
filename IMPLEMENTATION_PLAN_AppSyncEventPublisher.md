# Implementation Plan: AppSyncEventPublisher Refactoring

## Current State Analysis

### LocalWebService Configuration Flow
The LocalWebService retrieves AppSync Events API configuration at startup from CloudFormation stack outputs:

**File**: `Startup.g.cs` (lines 106-189)

1. Reads `APPSYNC_EVENTS_API_TYPE` environment variable (defaults to "Tenant")
2. Queries CloudFormation stack `{systemKey}---service` for outputs
3. Finds outputs ending with `EventsApiHttpDomain` and `EventsApiApiKey`
4. Stores them in IConfiguration as:
   - `AWS:AppSync:{apiName}EventsApi:HttpDomain`
   - `AWS:AppSync:{apiName}EventsApi:ApiKey`

**Example**: For TenantEventsApi:
- Output: `TenantEventsApiHttpDomain` → Config: `AWS:AppSync:TenantEventsApi:HttpDomain`
- Output: `TenantEventsApiApiKey` → Config: `AWS:AppSync:TenantEventsApi:ApiKey`

### AppSyncEventPublisher Current Issues

**File**: `AppSyncEventPublisher.cs`

1. **Hard-coded dual configuration** (lines 19-22):
   ```csharp
   private readonly string? _tenantEventApiHttpDomain;
   private readonly string? _consumerEventApiHttpDomain;
   private readonly string? _tenantEventApiKey;
   private readonly string? _consumerEventApiKey;
   ```

2. **Always uses Tenant** (lines 83, 129):
   ```csharp
   var eventApiHttpDomain = _tenantEventApiHttpDomain;  // Line 83
   var apiKey = _tenantEventApiKey;  // Line 129
   ```

3. **ConsumerEventsApi config loaded but never used**

### AppRunner Current State

**File**: `sam.service.apprunner.yaml` (lines 27-28)
```yaml
- Name: AWS__AppSync__EventApiId
  Value: !Ref __AppSyncEventsApiName__
```

**Problem**: Passes EventApiId reference, not the actual HttpDomain and ApiKey values.

### CloudFormation Stack Outputs

The CloudFormation template must be generating these outputs for LocalWebService to read:
- `TenantEventsApiHttpDomain`
- `TenantEventsApiApiKey`
- `ConsumerEventsApiHttpDomain`
- `ConsumerEventsApiApiKey`

## Proposed Solution

### Strategy: Single Events API Configuration Per Container

Each container (AppRunner or LocalWebService) should use **ONE** Events API configuration path, selected based on deployment/environment.

### Key Insight from LocalWebService

LocalWebService uses `APPSYNC_EVENTS_API_TYPE` environment variable to select which API to use from the stack outputs. We should leverage this pattern in AppSyncEventPublisher.

## Implementation Plan

### Phase 1: Update AppSyncEventPublisher (Backward Compatible)

**File**: `Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs`

**Changes**:

1. **Replace dual fields with single fields**:
   ```csharp
   private readonly string? _eventApiHttpDomain;
   private readonly string? _eventApiKey;
   ```

2. **Update constructor logic** (lines 35-44):
   ```csharp
   // Read Events API type from environment (same as LocalWebService)
   var eventsApiType = _configuration["APPSYNC_EVENTS_API_TYPE"] ?? "Tenant";

   // Try new unified configuration first (for future AppRunner deployments)
   _eventApiHttpDomain = _configuration["AWS:AppSync:EventsApi:HttpDomain"];
   _eventApiKey = _configuration["AWS:AppSync:EventsApi:ApiKey"];

   // Fallback to type-specific configuration (current LocalWebService pattern)
   if (string.IsNullOrEmpty(_eventApiHttpDomain))
   {
       _eventApiHttpDomain = _configuration[$"AWS:AppSync:{eventsApiType}EventsApi:HttpDomain"];
       _eventApiKey = _configuration[$"AWS:AppSync:{eventsApiType}EventsApi:ApiKey"];

       if (!string.IsNullOrEmpty(_eventApiHttpDomain))
       {
           _logger.LogInformation("Using {ApiType}EventsApi configuration", eventsApiType);
       }
   }
   else
   {
       _logger.LogInformation("Using unified EventsApi configuration");
   }

   // Region resolution with fallbacks
   _region = _configuration["AWS:AppSync:EventsApi:Region"]
       ?? _configuration["AWS_REGION"]
       ?? _configuration["AWS:Region"]
       ?? "us-east-1";

   // Validation logging
   if (string.IsNullOrEmpty(_eventApiHttpDomain))
   {
       _logger.LogWarning("AppSync Events API HttpDomain not configured. Events will not be published.");
   }
   else
   {
       _logger.LogInformation("AppSync Events Publisher initialized with domain: {Domain}", _eventApiHttpDomain);
   }

   if (string.IsNullOrEmpty(_eventApiKey) && UseApiKeyAuth)
   {
       _logger.LogWarning("AppSync Events API Key not configured.");
   }
   ```

3. **Update PublishChatEventAsync** (line 83):
   ```csharp
   // Remove:
   var eventApiHttpDomain = _tenantEventApiHttpDomain;

   // Replace with:
   if (string.IsNullOrEmpty(_eventApiHttpDomain))
   ```

4. **Update PublishEventAsync** (line 129):
   ```csharp
   // Remove:
   var apiKey = _tenantEventApiKey;

   // Replace with:
   if (!string.IsNullOrEmpty(_eventApiKey))
   {
       httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", _eventApiKey);
   }
   ```

**Benefits**:
- ✅ Works with current LocalWebService (reads `{ApiType}EventsApi` config)
- ✅ Forward compatible with unified `EventsApi` config
- ✅ Uses `APPSYNC_EVENTS_API_TYPE` like LocalWebService does
- ✅ No breaking changes to existing deployments

**Testing**:
- LocalWebService will continue to work (reads from stack outputs)
- AppRunner will need Phase 2 changes

### Phase 2: Update LocalWebService Startup (Optional Enhancement)

**File**: `LocalWebService/Startup.g.cs`

**Goal**: After reading stack outputs, also set unified `EventsApi` configuration based on selected type.

**Changes** (around line 152):
```csharp
// If this matches the requested API type, also set it as the default
if (apiName.Equals(eventsApiType, StringComparison.OrdinalIgnoreCase))
{
    logger.LogInformation($"Setting {apiName}EventsApi as the active Events API");

    // NEW: Also set unified configuration
    _configuration["AWS:AppSync:EventsApi:HttpDomain"] = output.OutputValue;
}
```

And around line 172:
```csharp
// Store in configuration
_configuration[$"AWS:AppSync:{apiName}EventsApi:ApiKey"] = output.OutputValue;

// NEW: If this matches the active type, also set unified config
if (apiName.Equals(eventsApiType, StringComparison.OrdinalIgnoreCase))
{
    _configuration["AWS:AppSync:EventsApi:ApiKey"] = output.OutputValue;
}
```

**Benefits**:
- ✅ LocalWebService will use new unified config path
- ✅ Prepares for eventual removal of type-specific paths
- ✅ Backward compatible (still sets type-specific paths)

**Testing**:
- LocalWebService should log "Using unified EventsApi configuration"

### Phase 3: Update CloudFormation Template for AppRunner

**File**: `AWSTemplates/Snippets/sam.service.apprunner.yaml`

**Current** (lines 27-28):
```yaml
- Name: AWS__AppSync__EventApiId
  Value: !Ref __AppSyncEventsApiName__
```

**New**:
```yaml
- Name: AWS__AppSync__EventsApi__HttpDomain
  Value: !GetAtt __AppSyncEventsApiName__.Dns.Http
- Name: AWS__AppSync__EventsApi__ApiKey
  Value: !GetAtt __AppSyncEventsApiName__ApiKey.ApiKey
- Name: AWS__AppSync__EventsApi__Region
  Value: !Ref AWS::Region
```

**Key Point**: `__AppSyncEventsApiName__` is a template placeholder that gets replaced during generation with either:
- `TenantEventsApi` for TenantApi containers
- `ConsumerEventsApi` for ConsumerApi containers

**Benefits**:
- ✅ Each container gets its specific Events API configuration
- ✅ Direct values, no runtime AWS API calls needed
- ✅ Uses unified config path

**Testing**:
- After regenerating CloudFormation template and deploying
- AppRunner containers should log "Using unified EventsApi configuration"

### Phase 4: CloudFormation Stack Outputs (Verify/Document)

**Need to verify**: Where are these outputs defined?
- `TenantEventsApiHttpDomain`
- `TenantEventsApiApiKey`
- `ConsumerEventsApiHttpDomain`
- `ConsumerEventsApiApiKey`

**Likely location**: Main CloudFormation template that defines AppSync Events APIs

**Action**: Document the output definitions (no changes needed for Phase 1-3)

## Migration Path

### Step 1: Code Changes (This PR)
- [x] Update AppSyncEventPublisher with backward-compatible logic
- [ ] Update LocalWebService Startup to set unified config (optional)
- [ ] Test LocalWebService locally

### Step 2: CloudFormation Changes (Next PR)
- [ ] Update sam.service.apprunner.yaml snippet
- [ ] Regenerate CloudFormation templates
- [ ] Deploy to dev environment
- [ ] Verify AppRunner containers work with new config

### Step 3: Cleanup (Future PR - after verification)
- [ ] Remove fallback logic from AppSyncEventPublisher
- [ ] Remove type-specific config paths from LocalWebService Startup
- [ ] Remove `APPSYNC_EVENTS_API_TYPE` environment variable
- [ ] Update documentation

## Testing Plan

### Phase 1 Testing (LocalWebService)

1. **No changes to launchSettings.json needed** - keeps `APPSYNC_EVENTS_API_TYPE: Tenant`

2. **Start LocalWebService**:
   ```bash
   cd Service/LocalWebService
   dotnet run
   ```

3. **Expected log output**:
   ```
   Using AppSync Events API Type: Tenant
   Found TenantEventsApiHttpDomain: xxx.appsync-api.region.amazonaws.com
   Found TenantEventsApiApiKey: da2-xxx...
   Using TenantEventsApi configuration
   AppSync Events Publisher initialized with domain: xxx.appsync-api.region.amazonaws.com
   ```

4. **Test chat event publishing**:
   - Send a chat message
   - Verify event is published
   - Check client receives event

### Phase 3 Testing (AppRunner)

1. **Deploy updated CloudFormation stack**

2. **Check container logs** for:
   ```
   Using unified EventsApi configuration
   AppSync Events Publisher initialized with domain: xxx.appsync-api.region.amazonaws.com
   ```

3. **Test chat event publishing** through deployed API

4. **Verify no errors** in CloudWatch Logs

## Rollback Plan

### If Phase 1 fails:
- Revert AppSyncEventPublisher changes
- LocalWebService continues working as before

### If Phase 3 fails:
- Revert CloudFormation template changes
- AppRunner falls back to type-specific config (via AppSyncEventPublisher fallback logic)

## Files to Modify

### Phase 1 (This PR):
1. ✅ `Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs`
2. ⏳ `Service/LocalWebService/Startup.g.cs` (optional enhancement)
3. ⏳ `Service/IMPLEMENTATION_PLAN_AppSyncEventPublisher.md` (this document)

### Phase 3 (Future PR):
1. `Service/AWSTemplates/Snippets/sam.service.apprunner.yaml`
2. Regenerate CloudFormation templates (if using generation)

## Questions to Resolve

1. **CloudFormation template generation**: How are the templates generated? Is there a build command?

2. **Stack outputs location**: Where are `TenantEventsApiHttpDomain` and `TenantEventsApiApiKey` outputs defined?

3. **Deployment process**: What's the standard deployment process for AppRunner containers?

4. **Multiple environments**: Are there dev/staging/prod environments to test in sequence?

## Success Criteria

### Phase 1:
- ✅ LocalWebService starts without errors
- ✅ Events publish successfully from LocalWebService
- ✅ Client receives events
- ✅ Logs show "Using TenantEventsApi configuration"

### Phase 3:
- ✅ AppRunner containers start without errors
- ✅ Events publish successfully from AppRunner
- ✅ Logs show "Using unified EventsApi configuration"
- ✅ No CloudWatch errors related to AppSync Events

## Next Steps

1. **Review this plan** - Ensure approach is correct
2. **Answer questions** - Resolve open questions above
3. **Implement Phase 1** - Update AppSyncEventPublisher
4. **Test locally** - Verify LocalWebService works
5. **Plan Phase 3** - Schedule CloudFormation deployment

---

## Implementation Status

✅ **COMPLETED** - 2025-01-11

This plan has been successfully implemented with the following outcomes:

### What Was Implemented

1. ✅ **Phase 1**: AppSyncEventPublisher refactored with unified configuration and backward-compatible fallback
2. ✅ **Phase 2**: LocalWebService Startup.g.cs updated to set both unified and type-specific configuration
3. ✅ **Phase 3**: CloudFormation template updated to pass direct values via !GetAtt

### Files Modified

1. `Service/Schemas/ChatSchemaRepo/Services/AppSyncEventPublisher.cs` - Updated to use single configuration with fallback
2. `Service/ProjectTemplates/WebApi/Startup.g.cs` - Enhanced to set unified config alongside type-specific config
3. `Service/AWSTemplates/Snippets/sam.service.apprunner.yaml` - Changed from EventApiId reference to direct HttpDomain and ApiKey values

### Testing Results

- ✅ Build succeeded
- ✅ Local testing confirmed working
- ✅ Configuration fallback logic validated

### Additional Work

Following this refactoring, a more comprehensive abstraction layer was implemented:
- Created two-layer architecture (IChatEventPublisher domain layer, IWsEventPublisher transport layer)
- Simplified ChatManagerService event publishing (83% code reduction)
- Added platform independence for future SignalR or other transport implementations
- See `ABSTRACTION_COMPLETE.md` for details

### Related Documents

- `IMPLEMENTATION_COMPLETE.md` - Summary of configuration refactoring completion
- `PLAN_EventPublisher_Abstraction.md` - Plan for abstraction layer implementation
- `ABSTRACTION_COMPLETE.md` - Summary of abstraction implementation
- `ChatEvents.md` - Updated documentation with new architecture
