# AppSync Events Setup for Local Development

## Overview
This guide explains how to configure AppSync Events for real-time chat updates in local development.

## Prerequisites
1. AWS account with appropriate permissions
2. Service stack deployed to AWS (contains AppSync Events API)
3. AWS credentials configured locally (for LocalWebService)

## Step 1: Deploy Service Stack

If you haven't already deployed the service stack:

```powershell
cd Service/AWSTemplates
Deploy-ServiceAws
```

This will create the AppSync Events API and output the required configuration values.

## Step 2: Get AppSync Events Configuration

After deployment, get the stack outputs:

```powershell
# Replace with your stack name
$stackName = "your-stack-name"
aws cloudformation describe-stacks --stack-name $stackName --query 'Stacks[0].Outputs'
```

Look for outputs for each Event API. There are two Event APIs in the stack:

### TenantEventsApi (for store/admin chat)
- `TenantEventsApi` - The WebSocket URL for clients (e.g., `https://xxx.appsync-api.us-east-1.amazonaws.com/event`)
- `TenantEventsApiAuth` - The authentication type (e.g., "tenantauth")
- `TenantEventsApiHttpDomain` - The HTTP domain for publishing (e.g., `xxx.appsync-api.us-east-1.amazonaws.com`)
- `TenantEventsApiApiKey` - The API Key for authentication (e.g., `da2-xxxxxxxxxxxxx`)

### ConsumerEventsApi (for consumer chat)
- `ConsumerEventsApi` - The WebSocket URL for clients
- `ConsumerEventsApiAuth` - The authentication type (e.g., "consumerauth")
- `ConsumerEventsApiHttpDomain` - The HTTP domain for publishing
- `ConsumerEventsApiApiKey` - The API Key for authentication

## Step 3: Configure LocalWebService

Update `/Service/LocalWebService/appsettings.Development.json`:

```json
{
  "AWS": {
    "Region": "us-east-1",
    "AppSync": {
      "TenantEventsApi": {
        "HttpDomain": "xxx.appsync-api.us-east-1.amazonaws.com",
        "ApiKey": "da2-your-tenant-api-key-here"
      },
      "ConsumerEventsApi": {
        "HttpDomain": "xxx.appsync-api.us-east-1.amazonaws.com",
        "ApiKey": "da2-your-consumer-api-key-here"
      }
    }
  }
}
```

**Note**: Each Event API has its own API Key for authentication when publishing events. Get these values from the CloudFormation stack outputs (`TenantEventsApiHttpDomain`, `TenantEventsApiApiKey`, etc.).

## Step 5: Test Event Publishing

1. Start LocalWebService:
```powershell
cd Service/LocalWebService
dotnet run
```

2. Send a chat message through the API

3. Check the logs for:
```
Successfully published event to AppSync Events API: Message_received for chat: {chatId}
```

If you see warnings about missing configuration, double-check Step 3.

## Step 6: Test Client Connection

1. The client automatically loads Events API URLs from `/config` endpoint
2. Client connects to the WebSocket URL from CloudFormation outputs
3. Watch for real-time updates in the chat interface

## Architecture

```
┌─────────────────┐
│  LocalWebService│
│                 │
│  Publishes via  │──HTTP POST──▶ ┌──────────────────┐
│  AWS SigV4      │               │  AppSync Events  │
└─────────────────┘               │      API         │
                                  └──────────────────┘
                                           │
                                           │ WebSocket
                                           ▼
                                  ┌──────────────────┐
                                  │   WASM Client    │
                                  │   MAUI Client    │
                                  └──────────────────┘
```

## Troubleshooting

### "AppSync Event API HTTP Domain not configured"
- Check appsettings.Development.json has correct values for both TenantEventsApi and ConsumerEventsApi
- Verify you're running in Development environment

### "Failed to publish event - 403 Forbidden"
- Verify the API Key is correct in appsettings.Development.json
- Check that API_KEY authentication is enabled in the Event API configuration
- Ensure the API Key hasn't expired (default: 365 days)

### Client not receiving events
- Verify client is authenticated with correct auth token
- Check WebSocket URL matches the deployed Events API
- Look for connection errors in browser console

### Events published but not received
- Verify channel path matches: `/chat/{chatId}`
- Check client is subscribed to the correct channel
- Confirm auth type matches between service and client

## Cost Considerations

AppSync Events pricing:
- Messages published: $1.00 per million
- Connection minutes: $0.08 per million

For local development, costs are typically < $1/month.

## Production vs Local Development

| Aspect | Production | Local Dev |
|--------|-----------|-----------|
| Service | AppRunner/Fargate | LocalWebService |
| AWS Calls | Lambda → AppSync | Local → AppSync |
| Client | Web/Mobile | Same (points to AWS) |
| Auth | Cognito JWT | Cognito JWT |

Both environments publish to the same AppSync Events API in AWS.
