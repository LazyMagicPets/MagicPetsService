# Multi-Authenticator Strategy

## Overview

The MagicPets service implements a dynamic multi-authenticator strategy that allows a single service container to authenticate requests against multiple AWS Cognito User Pools. This enables true multi-tenancy where different user populations (tenants, consumers, partners) can use the same API infrastructure while maintaining separate identity stores.

## Architecture

### Dynamic Discovery

Authenticators are discovered automatically at application startup by scanning environment variables:

**Pattern:** `LZ_AUTH_{NAME}_USERPOOLID`

Where `{NAME}` becomes the authenticator identifier (converted to lowercase).

**Example:**
```bash
LZ_AUTH_TENANTAUTH_USERPOOLID=us-east-1_TENANT123
LZ_AUTH_CONSUMERAUTH_USERPOOLID=us-east-1_CONSUMER456
LZ_AUTH_PARTNERAUTH_USERPOOLID=eu-west-1_PARTNER789
```

This configuration registers three authenticators:
- `tenantauth` - Validates JWTs from tenant User Pool
- `consumerauth` - Validates JWTs from consumer User Pool
- `partnerauth` - Validates JWTs from partner User Pool

### Region Configuration

**Default Region:** Set via `AWS_REGION` environment variable (defaults to `us-east-1`)

**Per-Authenticator Override:** Use `LZ_AUTH_{NAME}_REGION` to specify a different region for a specific authenticator:

```bash
AWS_REGION=us-east-1
LZ_AUTH_PARTNERAUTH_USERPOOLID=eu-west-1_PARTNER789
LZ_AUTH_PARTNERAUTH_REGION=eu-west-1
```

### Backward Compatibility

If no `LZ_AUTH_*` variables are found, the system falls back to legacy configuration:

```bash
AWS_REGION=us-east-1
COGNITO_USER_POOL_ID=us-east-1_LEGACY123
```

This registers a single authenticator named `default`.

## Request Authentication Flow

### With `lz-authname` Header

When a client specifies which authenticator to use:

```http
GET /api/endpoint HTTP/1.1
Authorization: Bearer <jwt_token>
lz-authname: tenantauth
```

**Flow:**
1. Middleware reads `lz-authname` header
2. Validates it's a registered authenticator (returns 400 if invalid)
3. Attempts authentication using only that scheme
4. On success: Sets `HttpContext.User`
5. On failure: Logs warning, continues without authentication

### Without `lz-authname` Header (Auto-Detection)

When a client doesn't specify the authenticator:

```http
GET /api/endpoint HTTP/1.1
Authorization: Bearer <jwt_token>
```

**Flow:**
1. Middleware tries each registered authenticator in sequence
2. First successful authentication wins
3. On success:
   - Sets `HttpContext.User`
   - **Adds `lz-authname` header to request** for downstream code
4. On failure: Request continues unauthenticated (unless endpoint requires auth)

**Example:**
```
Try tenantauth → JWT validation fails (wrong issuer)
Try consumerauth → JWT validation succeeds!
→ Sets context.User
→ Adds header: lz-authname: consumerauth
→ Downstream code can read which authenticator was used
```

## CORS Configuration

The `lz-authname` header is included in CORS configuration:

**Allowed Headers:** Clients can send `lz-authname`
**Exposed Headers:** Servers can send `lz-authname` back to clients

This enables web applications to:
1. Send authenticator preference in requests
2. Receive which authenticator was used in responses

## Code Integration

### Reading the Authenticator Used

In controller methods, you can determine which authenticator validated the request:

```csharp
public async Task<ActionResult> MyEndpoint()
{
    var authName = Request.Headers["lz-authname"].FirstOrDefault();

    if (authName == "tenantauth")
    {
        // This is a tenant user
    }
    else if (authName == "consumerauth")
    {
        // This is a consumer user
    }

    // Standard authorization logic
    var callerInfo = await StoreModuleAuthorization.GetCallerInfoAsync(this.Request);
    // ...
}
```

### Anonymous Endpoints

Endpoints marked with `[AllowAnonymous]` skip authorization:

```csharp
[AllowAnonymous]
[HttpPost, Route("fingerprint/create")]
public async Task<ActionResult<Fingerprint>> CreateFingerprint([FromBody] Fingerprint body)
{
    // Request might be authenticated or anonymous
    if (HttpContext.User.Identity.IsAuthenticated)
    {
        var authName = Request.Headers["lz-authname"].FirstOrDefault();
        var callerInfo = await GetCallerInfoAsync(this.Request);
        // Handle authenticated user
    }
    else
    {
        // Handle anonymous user
        var callerInfo = new CallerInfo { IsAnonymous = true };
    }

    return await FingerprintRepo.CreateAsync(callerInfo, body);
}
```

## Implementation Details

### Components

1. **AuthenticatorConfig.g.cs**
   - POCO class holding UserPoolId and Region
   - Used to store discovered authenticator configuration

2. **Program.g.cs - Discovery Function**
   - `DiscoverAuthenticators(string defaultRegion)` - Scans environment variables
   - Uses regex to match `LZ_AUTH_{NAME}_USERPOOLID` pattern
   - Returns dictionary of authenticator configurations

3. **Program.g.cs - Registration**
   - Registers each authenticator as a named JWT Bearer scheme
   - Configures JWT validation parameters (Authority, TokenValidationParameters)
   - Adds `OnTokenValidated` event to set `lz-authname` header
   - Stores list of authenticator names in DI container for middleware

4. **Program.g.cs - Middleware**
   - Custom middleware runs after CORS, before authentication
   - Handles both explicit (`lz-authname` present) and auto-detection flows
   - Comprehensive logging for debugging
   - Returns 400 for invalid `lz-authname` values

### JWT Validation

Each authenticator validates JWTs using standard ASP.NET Core JWT Bearer authentication:

- **Authority:** `https://cognito-idp.{region}.amazonaws.com/{userPoolId}`
- **JWKS Endpoint:** `{Authority}/.well-known/jwks.json`
- **Token Validation:**
  - Issuer: Validated against Authority
  - Audience: Not validated (common for Cognito)
  - Lifetime: Validated (exp claim)
  - Signature: Validated using JWKS public keys

**Caching:** JWKS keys are cached by the JWT middleware for 24 hours per Authority URL.

## Deployment Configuration

### Docker

```bash
docker run \
  -e AWS_REGION=us-east-1 \
  -e LZ_AUTH_TENANTAUTH_USERPOOLID=us-east-1_TENANT123 \
  -e LZ_AUTH_CONSUMERAUTH_USERPOOLID=us-east-1_CONSUMER456 \
  myimage:latest
```

### AWS App Runner / CloudFormation

```yaml
Environment:
  - Name: AWS_REGION
    Value: us-east-1
  - Name: LZ_AUTH_TENANTAUTH_USERPOOLID
    Value: !GetAtt TenantUserPool.Outputs.UserPoolId
  - Name: LZ_AUTH_CONSUMERAUTH_USERPOOLID
    Value: !GetAtt ConsumerUserPool.Outputs.UserPoolId
```

### Kubernetes

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: auth-config
data:
  AWS_REGION: "us-east-1"
  LZ_AUTH_TENANTAUTH_USERPOOLID: "us-east-1_TENANT123"
  LZ_AUTH_CONSUMERAUTH_USERPOOLID: "us-east-1_CONSUMER456"
```

## Logging

The system logs authenticator registration and authentication attempts:

**Startup Logs:**
```
Registered authenticator: tenantauth (Region: us-east-1, Pool: us-east-1_TENANT123)
Registered authenticator: consumerauth (Region: us-east-1, Pool: us-east-1_CONSUMER456)
```

**Request Logs (Debug Level):**
```
Using authentication scheme from header: tenantauth
No lz-authname header found, trying all registered authenticators
Authentication failed with scheme tenantauth: The signature is invalid
```

**Request Logs (Info Level):**
```
Successfully authenticated with scheme: consumerauth
```

**Request Logs (Warning Level):**
```
Invalid lz-authname specified: invalid_auth
Authentication failed for scheme: tenantauth
```

## Security Considerations

### JWT Validation
- All JWTs are validated against their respective Cognito User Pool's public keys
- Token lifetime (exp claim) is enforced
- Signature verification ensures token integrity

### No Token Reuse
- A JWT issued by `tenantauth` User Pool cannot be used with `consumerauth` endpoints
- Each authenticator validates against its own Authority/Issuer

### Denial of Service Protection
- Auto-detection tries authenticators sequentially, not in parallel
- JWKS caching prevents repeated calls to AWS
- Invalid `lz-authname` returns 400 immediately without attempting authentication

### Anonymous Access
- Endpoints must explicitly opt-in with `[AllowAnonymous]`
- Default behavior requires authentication
- Anonymous requests can be handled differently in business logic

## Troubleshooting

### No Authenticators Registered

**Error:** `InvalidOperationException: No authenticators configured`

**Solution:** Set at least one environment variable:
```bash
LZ_AUTH_TENANTAUTH_USERPOOLID=us-east-1_ABC123
```

### Invalid lz-authname

**Response:** `400 Bad Request`
```json
{
  "error": "Invalid lz-authname",
  "value": "invalid_auth",
  "validValues": ["tenantauth", "consumerauth"]
}
```

**Solution:** Use one of the valid authenticator names shown in `validValues`

### Authentication Always Fails

**Check:**
1. JWT is valid and not expired
2. JWT was issued by one of the configured User Pools
3. User Pool ID and Region are correct
4. Network allows access to `cognito-idp.{region}.amazonaws.com`

**Enable Debug Logging:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore.Authentication": "Debug"
    }
  }
}
```

## Benefits

1. **Scalability:** Support unlimited authenticators without code changes
2. **Flexibility:** Different user populations, different identity stores
3. **Multi-Region:** Each authenticator can use a different AWS region
4. **Developer Experience:** Auto-detection means clients don't need to specify authenticator
5. **Debugging:** Comprehensive logging at all levels
6. **Backward Compatible:** Supports legacy single authenticator configuration
7. **Performance:** JWKS caching minimizes calls to AWS
8. **Security:** Each authenticator independently validates JWTs
