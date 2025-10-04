# Host Header Configuration for Keep-Alive Requests

## Overview

The `Host` header from incoming requests is used by `ChatManagerService` to dynamically determine the service URL for internal keep-alive HTTP requests. This header is automatically present in all HTTP/1.1 requests and is captured during chat initialization.

## Purpose

`ChatManagerService` maintains a single long-polling HTTP request to prevent AWS App Runner from scaling down while chat sessions are active. This request needs to call back to the same service instance, requiring knowledge of the service's URL.

## How It Works

The standard HTTP `Host` header is automatically present in all requests:

```
Host: <hostname>[:<port>]
```

### Examples

- **Production**: `Host: chat.magicpets.com`
- **Staging**: `Host: chat-staging.magicpets.com`
- **Local**: `Host: localhost:8080`

## Implementation

### All Environments

No infrastructure configuration is required. The standard HTTP `Host` header is automatically included in all HTTP/1.1 requests by clients and proxies.

### Local Development (LocalWebService)

The `Host` header is automatically set to `localhost:8080` (or whatever port is configured) by the HTTP client. If for any reason it's not present, ChatManagerService falls back to `http://localhost:8080`.

### AWS Environments (CloudFront, API Gateway, ALB)

The `Host` header is automatically preserved and forwarded by AWS infrastructure:

- **CloudFront**: Forwards the `Host` header from the original request
- **API Gateway**: Includes the `Host` header in requests to backend services
- **Application Load Balancer**: Preserves the `Host` header from incoming requests

No custom configuration is needed in any of these services.

## Flow Diagram

```
┌─────────────────┐
│  Client Request │
│  Host: chat.app │
└────────┬────────┘
         │
         ▼
┌──────────────────────────┐
│  ChatModuleAuthorization │
│  GetLzHeaders()          │
│  Captures Host header    │
└────────┬─────────────────┘
         │
         ▼
┌─────────────────────────┐
│  CallerInfo.Headers     │
│  ["Host"]               │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  ChatManagerService     │
│  InitializeChatAsync()  │
│  Stores CallerInfo      │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  ConnectionChat         │
│  CallerInfo stored      │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  InitiateKeepAliveAsync │
│  Reads Host from        │
│  CallerInfo.Headers     │
│  Constructs URL         │
└────────┬────────────────┘
         │
         ▼
┌─────────────────────────┐
│  HTTP POST              │
│  https://{host}/...     │
│  /keepalive             │
└─────────────────────────┘
```

## Code References

### ChatModuleAuthorization.cs
Captures the `Host` header from incoming requests:
```csharp
protected override Dictionary<string, string> GetLzHeaders(HttpRequest request)
{
    var headers = base.GetLzHeaders(request);

    // Add Host header for keep-alive requests
    headers["Host"] = request.Host.ToString();

    return headers;
}
```

### ChatManagerService.cs
Uses the header to construct keep-alive URLs:
```csharp
private string GetServiceHost()
{
    var firstChat = _chats.Values.FirstOrDefault();
    if (firstChat?.CallerInfo?.Headers != null &&
        firstChat.CallerInfo.Headers.TryGetValue("Host", out var host) &&
        !string.IsNullOrEmpty(host))
    {
        // Determine scheme based on host
        var scheme = host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? "http" : "https";
        return $"{scheme}://{host}";
    }

    // Fallback to localhost for local development
    return "http://localhost:8080";
}
```

## Testing

### Local Development
1. Start LocalWebService: `dotnet run`
2. Create a chat: `POST http://localhost:8080/ChatModule/chat`
3. Check logs for: `"Initiating keep-alive request for ChatManagerService at http://localhost:8080"`

### Production
1. Deploy with CloudFront/ALB configuration
2. Create a chat via API
3. Monitor CloudWatch logs for keep-alive requests
4. Verify URL uses production domain name

## Troubleshooting

**Issue**: Keep-alive requests fail with connection errors
**Solution**: The `Host` header should be automatically present in all HTTP/1.1 requests. Check ChatManagerService logs to see what host is being used for keep-alive requests.

**Issue**: Header not captured in CallerInfo
**Solution**: Verify `ChatModuleAuthorization.GetLzHeaders()` is capturing the Host header correctly.

**Issue**: Local development uses wrong host
**Solution**: The fallback to `localhost:8080` should work automatically. Check ChatManagerService logs to verify the host being used.

## Related Files

- `/Service/Modules/ChatModule/ChatModuleAuthorization.cs` - Header capture
- `/Service/Schemas/ChatSchemaRepo/Services/ChatManagerService.cs` - Header usage
- `/Service/Schemas/ChatSchemaRepo/Services/IChatManagerService.cs` - Interface
