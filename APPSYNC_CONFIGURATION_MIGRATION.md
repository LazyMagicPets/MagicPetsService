# AppSync Events Configuration Migration

## Overview

The `AppSyncEventPublisher` has been refactored to use a single Events API configuration per container instead of hard-coded dual configuration for both Tenant and Consumer APIs.

## Changes Made

### 1. AppSyncEventPublisher.cs
- **Removed**: Dual configuration fields (`_tenantEventApiHttpDomain`, `_consumerEventApiHttpDomain`, etc.)
- **Added**: Single configuration fields (`_eventApiHttpDomain`, `_eventApiKey`)
- **Added**: Backward compatibility fallback using `APPSYNC_EVENTS_API_TYPE` environment variable
- **Added**: Enhanced logging for configuration validation

### 2. CloudFormation Template (sam.service.apprunner.yaml)
- **Changed**: Environment variables now pass direct values instead of references
- **Old**: `AWS__AppSync__EventApiId` (reference requiring API calls)
- **New**:
  - `AWS__AppSync__EventsApi__HttpDomain` (!GetAtt HttpDomain)
  - `AWS__AppSync__EventsApi__ApiKey` (!GetAtt ApiKey)
  - `AWS__AppSync__EventsApi__Region` (!Ref AWS::Region)

### 3. LocalWebService Configuration (appsettings.Development.json)
- **Added**: New `AWS:AppSync:EventsApi` section with HttpDomain, ApiKey, Region
- **Kept**: Legacy `TenantEventsApi` configuration for backward compatibility during migration

## Configuration Priority

The code now reads configuration in the following priority order:

1. **New Format** (Preferred):
   ```json
   {
     "AWS": {
       "AppSync": {
         "EventsApi": {
           "HttpDomain": "xxx.appsync-api.region.amazonaws.com",
           "ApiKey": "da2-xxx...",
           "Region": "us-west-2"
         }
       }
     }
   }
   ```

2. **Legacy Format** (Backward compatibility):
   ```json
   {
     "APPSYNC_EVENTS_API_TYPE": "Tenant",
     "AWS": {
       "AppSync": {
         "TenantEventsApi": {
           "HttpDomain": "xxx.appsync-api.region.amazonaws.com",
           "ApiKey": "da2-xxx..."
         }
       }
     }
   }
   ```

3. **Region Fallback Order**:
   - `AWS:AppSync:EventsApi:Region`
   - `AWS_REGION`
   - `AWS:Region`
   - Default: `us-east-1`

## Local Development Setup

### Option 1: appsettings.Development.json (Current Approach)

The placeholder configuration is already in place. **You must update the API key**:

1. Get your AppSync Events API key from AWS Console or CloudFormation outputs
2. Update `appsettings.Development.json`:
   ```json
   {
     "AWS": {
       "AppSync": {
         "EventsApi": {
           "ApiKey": "YOUR_ACTUAL_API_KEY_HERE"
         }
       }
     }
   }
   ```

**Important**: Do NOT commit real API keys to git. Consider adding `appsettings.Development.json` to `.gitignore` or use User Secrets (Option 2).

### Option 2: .NET User Secrets (Recommended for Security)

Use .NET User Secrets to store API keys outside of the repository:

```bash
cd Service/LocalWebService

# Set the configuration values
dotnet user-secrets set "AWS:AppSync:EventsApi:HttpDomain" "24njcduygfd35m34apqhaaqs6e.appsync-api.us-west-2.amazonaws.com"
dotnet user-secrets set "AWS:AppSync:EventsApi:ApiKey" "da2-YOUR_ACTUAL_KEY"
dotnet user-secrets set "AWS:AppSync:EventsApi:Region" "us-west-2"
```

User secrets are stored in:
- **Windows**: `%APPDATA%\Microsoft\UserSecrets\<user_secrets_id>\secrets.json`
- **Linux/macOS**: `~/.microsoft/usersecrets/<user_secrets_id>/secrets.json`

To enable user secrets, add this to `LocalWebService.csproj`:
```xml
<PropertyGroup>
  <UserSecretsId>magicpets-localwebservice</UserSecretsId>
</PropertyGroup>
```

### Option 3: Environment Variables

Set environment variables in `launchSettings.json`:

```json
{
  "profiles": {
    "Service": {
      "environmentVariables": {
        "AWS__AppSync__EventsApi__HttpDomain": "xxx.appsync-api.region.amazonaws.com",
        "AWS__AppSync__EventsApi__ApiKey": "da2-xxx...",
        "AWS__AppSync__EventsApi__Region": "us-west-2"
      }
    }
  }
}
```

**Note**: Double underscores (`__`) in environment variables map to colons (`:`) in configuration keys.

## Deployment

### AppRunner Containers

No action required. The CloudFormation template changes will automatically:
1. Retrieve HttpDomain and ApiKey from AppSync Events API resources
2. Set environment variables when the container starts
3. Code will read the new configuration format

### Testing After Deployment

Check container logs for the startup message:
```
AppSync Events Publisher initialized with domain: xxx.appsync-api.region.amazonaws.com
```

If you see legacy configuration warnings:
```
Using legacy TenantEventsApi configuration. Please update to EventsApi configuration.
```

This means the container is using backward compatibility fallback. Update the CloudFormation template and redeploy.

## Migration Checklist

- [x] Update AppSyncEventPublisher.cs with single configuration + backward compatibility
- [x] Update CloudFormation template snippet (sam.service.apprunner.yaml)
- [x] Add new configuration format to appsettings.Development.json
- [ ] **ACTION REQUIRED**: Update API key in appsettings.Development.json or setup User Secrets
- [ ] Test LocalWebService with new configuration
- [ ] Regenerate CloudFormation templates (if using template generation)
- [ ] Deploy updated CloudFormation stack
- [ ] Deploy updated container images
- [ ] Verify events publishing in AppRunner containers
- [ ] Remove legacy configuration after verification period

## Troubleshooting

### "AppSync Events API HttpDomain not configured"
- Check that `AWS:AppSync:EventsApi:HttpDomain` is set in configuration
- For AppRunner: Verify CloudFormation environment variables
- For LocalWebService: Check appsettings.Development.json or user secrets

### "API Key not configured"
- Check that `AWS:AppSync:EventsApi:ApiKey` is set
- Verify the API key is not the placeholder value
- For AppRunner: Check CloudFormation GetAtt expression

### Events not publishing
- Check logs for configuration validation warnings
- Verify the HttpDomain format: `xxx.appsync-api.region.amazonaws.com`
- Verify API key starts with `da2-`
- Check IAM permissions if using IAM auth instead of API Key auth

## Benefits of New Approach

1. ✅ **Architectural Clarity**: Each container has exactly the configuration it needs
2. ✅ **Simplicity**: Single configuration path - no conditional logic
3. ✅ **Reusability**: Works with any Events API by changing configuration
4. ✅ **Consistency**: AppRunner and LocalWebService use identical configuration keys
5. ✅ **Performance**: No runtime AWS API calls to resolve EventApiId
6. ✅ **Backward Compatible**: Legacy configuration still works during migration period

## Future Cleanup

After all environments are migrated to the new configuration format:

1. Remove backward compatibility fallback code from AppSyncEventPublisher constructor
2. Remove legacy environment variables from CloudFormation template
3. Remove `TenantEventsApi` and `ConsumerEventsApi` sections from appsettings files
4. Remove `APPSYNC_EVENTS_API_TYPE` environment variable

Target completion: [TBD]
