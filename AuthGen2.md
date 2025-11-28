# CloudFront Multi-Auth Domain-Level Authentication - Complete Implementation Plan

## Executive Summary

Implement domain-level, multi-auth OIDC authentication with CloudFront routing for unlimited subdomains. Leverage existing LazyMagic infrastructure (ICallerInfo, LzAuthorization) and add cookie-based subtenant management.

**Key Insight:** Most infrastructure already exists - we mainly need:
1. New `/auth` app for centralized authentication
2. Cookie management for subtenant routing
3. Enhanced state parameter to track auth config
4. Minor modifications to existing CloudFront Request Function

---

## 1. Architecture Overview

### 1.1 Core Pattern

**Domain-Level Authentication:**
- All apps run at root domain: `lazymagicdev.click/store`
- Subdomains only for initial routing: `uptown.lazymagicdev.click` → redirects to auth
- After auth: User stays at root domain with subtenant in cookie

**Storage Strategy:**
- **Auth Tokens:** localStorage (OAuth standard, Microsoft OIDC compatible)
- **Subtenant:** Cookie `lz-subtenant=uptown` (CloudFront accessible, cross-subdomain)
- **Auth Config:** Tracked in state parameter during auth flow

**Multi-Auth Support:**
- Multiple Cognito User Pools per domain (ConsumerAuth, AdminAuth, TenantAuth)
- All use same callback URL: `lazymagicdev.click/callback`
- State parameter tracks which auth config initiated login

### 1.2 Complete Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 1. INITIAL REQUEST (Subdomain)                                             │
└─────────────────────────────────────────────────────────────────────────────┘
User → https://uptown.lazymagicdev.click/store

CloudFront Request Handler:
  - Extracts subtenant: "uptown"
  - Redirects to: https://lazymagicdev.click/auth?
                    subtenant=uptown&
                    returnUrl=/store
  - Note: NO tenantauth in query string - user will select!

┌─────────────────────────────────────────────────────────────────────────────┐
│ 2. AUTH APP - SIMPLEAUTH LANDING PAGE                                      │
└─────────────────────────────────────────────────────────────────────────────┘
App loads at: https://lazymagicdev.click/auth?subtenant=uptown&returnUrl=/store&app=StoreApp

**Optional Query Parameters:**
  - subtenant: If provided, skip subtenant selection
  - authname: If provided, skip authname selection
  - app: Target application identifier
  - returnUrl: Where to redirect after auth

**Flow:**
  a. Parse query params (subtenant, authname, app, returnUrl)

  b. IF subtenant NOT provided:
       → Fetch list of subtenants from GET /ActiveSubTenantModule/activesubtenant/list
       → Display subtenant selection UI
       → User selects subtenant
       → Set lz-subtenant cookie to selected value
     ELSE:
       → Set lz-subtenant cookie to provided subtenant value

  c. IF authname NOT provided:
       → Load configs from /config
       → Filter authConfigs based on selected subtenant (if needed)
       → Display authname selection UI
       → User selects authname (e.g., "ConsumerAuth", "AdminAuth")
       → Set lz-authname cookie to selected value
     ELSE:
       → Set lz-authname cookie to provided authname value

  d. Check localStorage for valid tokens for selected authname:
     IF valid tokens found:
       → Redirect to returnUrl (or /)
     ELSE:
       → Build OIDC authorize URL with selected authname config
       → Build state parameter with {authConfigName, subtenant, returnUrl, targetDomain, app}
       → Navigate to Cognito for authentication

┌─────────────────────────────────────────────────────────────────────────────┐
│ 3. COGNITO AUTHENTICATION                                                   │
└─────────────────────────────────────────────────────────────────────────────┘
User authenticates at Cognito Hosted UI

Cognito redirects to: https://lazymagicdev.click/callback?
                        code=AUTH_CODE&
                        state=BASE64_ENCODED_STATE

State contains:
  {
    authConfigName: "ConsumerAuth",
    subtenant: "uptown",
    returnUrl: "/store",
    targetDomain: "lazymagicdev.click"
  }

┌─────────────────────────────────────────────────────────────────────────────┐
│ 4. CALLBACK HANDLER (New - To Create)                                      │
└─────────────────────────────────────────────────────────────────────────────┘
/callback page:
  a. Parse state parameter → extract authConfigName
  b. Switch to correct auth context
  c. Exchange authorization code for tokens
  d. Store tokens in localStorage (Microsoft OIDC format)
  e. Store subtenant in cookie (lz-subtenant=uptown, Domain=.lazymagicdev.click)
  f. Redirect to: https://lazymagicdev.click/store

┌─────────────────────────────────────────────────────────────────────────────┐
│ 5. APPLICATION RUNS AT ROOT DOMAIN                                         │
└─────────────────────────────────────────────────────────────────────────────┘
App loads at: https://lazymagicdev.click/store

Initialization:
  a. Read tokens from localStorage (Microsoft OIDC)
  b. Read subtenant from cookie (lz-subtenant)
  c. Store subtenant in localStorage for easy access
  d. Configure HttpClient with:
     - Authorization header (Bearer token)
     - X-LazyMagic-Subtenant header (subtenant value)

API Requests:
  GET https://lazymagicdev.click/api/products
  Headers:
    - Authorization: Bearer {access_token}
    - X-LazyMagic-Subtenant: uptown
  Cookies:
    - lz-subtenant=uptown (auto-sent by browser)

Static Asset Requests:
  GET https://lazymagicdev.click/store/images/logo.png
  Cookies:
    - lz-subtenant=uptown (auto-sent by browser)
    - lz-authname=ConsumerAuth (auto-sent by browser)

  CloudFront Request Handler:
    - Reads lz-subtenant cookie value: "uptown"
    - Reads host header value: "lazymagicdev.click"
    - Constructs KVS key: "{subtenant}.{host}" = "uptown.lazymagicdev.click"
    - Calls GetConfig("uptown.lazymagicdev.click") to fetch configuration
    - Routes to subtenant-specific origin based on config
    - Adds lz-subtenant header to API requests
    - Adds lz-authname header to API requests (if cookie present)

Server API (MagicPetsService):
  LzAuthorization.AddConfigAsync():
    - Reads X-LazyMagic-Subtenant header (from HttpClient)
    - OR reads lz-subtenant cookie (from browser)
    - Sets CallerInfo.Subtenant
    - Repository uses CallerInfo.Subtenant for data access
```

---

## 2. Implementation Plan by Component

### Phase 1: Server-Side Enhancement (LazyMagic.Service.Authorization)

#### 2.1 Enhance LzAuthorization to Read Subtenant from Cookie

**File:** `LazyMagic.Service.Authorization/LzAuthorization.cs`

**Modify:** `AddConfigAsync()` method (lines 108-139)

**Changes:**
```csharp
protected virtual Task AddConfigAsync(HttpRequest request, ICallerInfo callerInfo)
{
    var configJson = request.Headers["lz-config"];
    var tenantId = request.Headers["lz-tenantid"];
    var authname = request.Headers["lz-authname"].FirstOrDefault();

    var tenancyConfig = new TenancyConfig(configJson!, tenantId!);

    // NEW: Read subtenant from multiple sources (priority order)
    // 1. Custom header (from HttpClient message handler)
    var subtenantFromHeader = request.Headers["lz-subtenant"].FirstOrDefault();
    if (!string.IsNullOrEmpty(subtenantFromHeader))
    {
        tenancyConfig.Subtenant = subtenantFromHeader;
    }
    // 2. Cookie (from browser, set by auth app)
    else if (request.Cookies.TryGetValue("lz-subtenant", out var subtenantFromCookie))
    {
        tenancyConfig.Subtenant = subtenantFromCookie;
    }

    // Existing code - populate CallerInfo
    callerInfo.TenantId = tenancyConfig.Id;
    callerInfo.System = tenancyConfig.System;
    callerInfo.Tenant = tenancyConfig.Tenant;
    callerInfo.Subtenant = tenancyConfig.Subtenant; // Now from header or cookie
    callerInfo.SystemDB = tenancyConfig.SystemDB;
    callerInfo.TenantDB = tenancyConfig.TenantDB;
    callerInfo.SubtenantDB = tenancyConfig.SubtenantDB;
    callerInfo.SystemAssets = tenancyConfig.SystemAssets;
    callerInfo.TenantAssets = tenancyConfig.TenantAssets;
    callerInfo.SubtenantAssets = tenancyConfig.SubtenantAssets;
    callerInfo.DefaultTenant = tenancyConfig.DefaultTenant;
    callerInfo.DefaultDB = tenancyConfig.DefaultDB;
    callerInfo.DefaultAssets = tenancyConfig.DefaultAssets;
    callerInfo.Authname = authname;

    return Task.CompletedTask;
}
```

**Testing:**
- Unit test: Mock HttpRequest with cookie
- Unit test: Mock HttpRequest with header
- Integration test: API call with cookie vs header

---

### Phase 2: Client-Side Infrastructure (LazyMagic Libraries)

#### 2.2 Subtenant Service

**New File:** `LazyMagic.Client.Base/Services/ISubtenantService.cs`
```csharp
namespace LazyMagic.Client.Base.Services;

public interface ISubtenantService
{
    Task<string?> GetSubtenantAsync();
    Task SetSubtenantAsync(string subtenant);
    Task ClearSubtenantAsync();
    Task<string> GetRootDomainAsync();
}
```

**New File:** `LazyMagic.Client.Base/Services/SubtenantService.cs`
```csharp
public class SubtenantService : ISubtenantService
{
    private readonly ILzJsUtilities _jsUtilities;

    public async Task<string?> GetSubtenantAsync()
    {
        // Try cookie first
        var subtenant = await _jsUtilities.GetCookieAsync("lz-subtenant");
        if (!string.IsNullOrEmpty(subtenant))
            return subtenant;

        // Fall back to localStorage
        return await _jsUtilities.LocalStorageGetItemAsync("lz-subtenant");
    }

    public async Task SetSubtenantAsync(string subtenant)
    {
        var rootDomain = await GetRootDomainAsync();

        // Store in cookie (cross-subdomain)
        await _jsUtilities.SetCookieAsync("lz-subtenant", subtenant, new CookieOptions
        {
            Domain = $".{rootDomain}",
            Path = "/",
            Secure = true,
            SameSite = "Lax",
            Days = 30
        });

        // Also store in localStorage (easy access)
        await _jsUtilities.LocalStorageSetItemAsync("lz-subtenant", subtenant);
    }

    public async Task<string> GetRootDomainAsync()
    {
        var hostname = await _jsUtilities.GetHostnameAsync();
        var parts = hostname.Split('.');

        // Return last 2 parts: "lazymagicdev.click" from "uptown.lazymagicdev.click"
        return parts.Length > 2
            ? string.Join('.', parts.TakeLast(2))
            : hostname;
    }
}
```

#### 2.3 HttpClient Message Handler for Subtenant

**New File:** `LazyMagic.Client.Base/Handlers/SubtenantMessageHandler.cs`
```csharp
public class SubtenantMessageHandler : DelegatingHandler
{
    private readonly ISubtenantService _subtenantService;

    public SubtenantMessageHandler(ISubtenantService subtenantService)
    {
        _subtenantService = subtenantService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var subtenant = await _subtenantService.GetSubtenantAsync();

        if (!string.IsNullOrEmpty(subtenant))
        {
            request.Headers.Add("X-LazyMagic-Subtenant", subtenant);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
```

#### 2.4 Enhanced State Parameter in BlazorOIDCService

**File:** `LazyMagic.OIDC.WASM/OIDC/BlazorOIDCService.cs`

**Modify:** `LoginAsync()` method (around line 318-325)
```csharp
// BEFORE:
var stateData = new {
    targetDomain = targetDomain,
    targetPath = targetPath
};

// AFTER:
var stateData = new {
    authConfigName = _oidcConfig.SelectedAuthConfig,  // NEW!
    subtenant = await _subtenantService.GetSubtenantAsync(),  // NEW!
    returnUrl = targetPath,
    targetDomain = targetDomain,
    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
};
```

#### 2.5 Extend LzJsUtilities for Cookie Operations

**File:** `LazyMagic.Client.Base/Utilities/LzJsUtilities.cs`

**Note:** Cookie functions already exist in `lzJsUtilities.js` (lines 203-345)
**Add:** C# wrapper methods
```csharp
public async Task<string?> GetCookieAsync(string name)
{
    return await InvokeSafeAsync<string>("getCookie", name);
}

public async Task SetCookieAsync(string name, string value, CookieOptions? options = null)
{
    await InvokeSafeVoidAsync("setCookie", name, value, options);
}

public async Task<string> GetHostnameAsync()
{
    return await InvokeSafeAsync<string>("eval", "window.location.hostname");
}
```

**Add:** CookieOptions model
```csharp
public class CookieOptions
{
    public string? Domain { get; set; }
    public string? Path { get; set; }
    public int? Days { get; set; }
    public bool Secure { get; set; }
    public string? SameSite { get; set; }
}
```

---

### Phase 3: Auth App (New Project)

#### 2.6 Create LazyMagic.Auth.WASM Project

**Project Structure:**
```
LazyMagic.Auth.WASM/
├── LazyMagic.Auth.WASM.csproj
├── Program.cs
├── App.razor
├── _Imports.razor
├── Pages/
│   ├── Index.razor (Main auth check page)
│   ├── Callback.razor (OAuth callback handler)
│   └── Logout.razor (Logout handler)
├── Services/
│   ├── AuthOrchestrator.cs
│   └── TokenExchangeService.cs
├── Models/
│   └── AuthFlowState.cs
└── wwwroot/
    ├── index.html
    ├── appsettings.json
    └── js/
        └── authHelper.js
```

**Program.cs:**
```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// Register LazyMagic services
builder.Services.AddLazyMagicClient();
builder.Services.AddLazyMagicOIDCWASM();

// Register auth app ViewModels and services
builder.Services.AddAuthAppViewModels();

var host = builder.Build();

// Initialize
await host.RunAsync();
```

**Config/ConfigureAuthAppViewModels.cs:**
```csharp
namespace AuthApp;

public static class ConfigureAuthAppViewModels
{
    public static IServiceCollection AddAuthAppViewModels(this IServiceCollection services)
    {
        // Register auth orchestrator and services
        services.AddScoped<AuthOrchestrator>();
        services.AddScoped<TokenExchangeService>();

        // Register HttpClient with authentication handler
        // Note: For auth app, we may not need authentication handler since this IS the auth app
        // But we follow the pattern for consistency
        services.AddScoped(sp =>
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(sp.GetRequiredService<ILzHost>().GetApiUrl(""))
            };
            return httpClient;
        });

        return services;
    }
}
```

**Pages/Index.razor (Auth Selection Page):**
```razor
@page "/auth"
@inject AuthOrchestrator AuthOrch
@inject NavigationManager Nav

<div class="auth-container">
    @if (_showAuthSelection)
    {
        <div class="auth-selection">
            <h2>Select Authentication Method</h2>
            <p class="subtitle">Choose how you would like to sign in:</p>

            <div class="auth-options">
                @foreach (var authConfig in _availableAuthConfigs)
                {
                    <button class="auth-button" @onclick="() => SelectAuth(authConfig.Key)">
                        <div class="auth-button-content">
                            <span class="auth-icon">@GetAuthIcon(authConfig.Key)</span>
                            <span class="auth-name">@GetAuthDisplayName(authConfig.Key)</span>
                            <span class="auth-description">@GetAuthDescription(authConfig.Key)</span>
                        </div>
                    </button>
                }
            </div>
        </div>
    }
    else
    {
        <div class="auth-processing">
            <h3>@_statusTitle</h3>
            <p>@_statusMessage</p>
            <div class="spinner"></div>
        </div>
    }
</div>

@code {
    private bool _showAuthSelection = false;
    private string _statusTitle = "Authenticating...";
    private string _statusMessage = "Checking authentication status...";
    private Dictionary<string, JObject> _availableAuthConfigs = new();

    [SupplyParameterFromQuery]
    public string? Subtenant { get; set; }

    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _statusMessage = "Loading authentication options...";
            StateHasChanged();

            // Load auth configs from /config
            await AuthOrch.LoadAuthConfigsAsync();
            _availableAuthConfigs = AuthOrch.GetAvailableAuthConfigs();

            _statusMessage = "Checking for existing credentials...";
            StateHasChanged();

            // Check if user has valid tokens for ANY auth config
            var (hasValidTokens, authConfigName) = await AuthOrch.HasAnyValidTokensAsync();

            if (hasValidTokens)
            {
                _statusTitle = "Welcome back!";
                _statusMessage = $"Valid credentials found for {authConfigName}. Redirecting...";
                StateHasChanged();

                // Store subtenant and redirect to app
                await AuthOrch.StoreSubtenantAsync(Subtenant);
                Nav.NavigateTo(ReturnUrl ?? "/", forceLoad: true);
            }
            else
            {
                // No valid tokens - show auth selection UI
                _showAuthSelection = true;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            _statusTitle = "Error";
            _statusMessage = $"Failed to load authentication options: {ex.Message}";
            StateHasChanged();
        }
    }

    private async Task SelectAuth(string authConfigName)
    {
        _showAuthSelection = false;
        _statusTitle = "Redirecting to login...";
        _statusMessage = $"Starting {GetAuthDisplayName(authConfigName)} authentication...";
        StateHasChanged();

        // Initiate login with selected auth config
        await AuthOrch.InitiateLoginAsync(Subtenant, authConfigName, ReturnUrl);
    }

    private string GetAuthDisplayName(string authConfigName)
    {
        return authConfigName switch
        {
            "ConsumerAuth" => "Consumer Login",
            "AdminAuth" => "Administrator Login",
            "TenantAuth" => "Tenant Login",
            _ => authConfigName
        };
    }

    private string GetAuthDescription(string authConfigName)
    {
        return authConfigName switch
        {
            "ConsumerAuth" => "For customers and end users",
            "AdminAuth" => "For system administrators",
            "TenantAuth" => "For tenant account managers",
            _ => "Authenticate with " + authConfigName
        };
    }

    private string GetAuthIcon(string authConfigName)
    {
        return authConfigName switch
        {
            "ConsumerAuth" => "👤",
            "AdminAuth" => "🔧",
            "TenantAuth" => "🏢",
            _ => "🔐"
        };
    }
}
```

**wwwroot/css/auth.css (styling for auth selection):**
```css
.auth-container {
    max-width: 600px;
    margin: 100px auto;
    padding: 40px;
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

.auth-selection h2 {
    text-align: center;
    margin-bottom: 10px;
    color: #333;
}

.auth-selection .subtitle {
    text-align: center;
    color: #666;
    margin-bottom: 40px;
}

.auth-options {
    display: flex;
    flex-direction: column;
    gap: 15px;
}

.auth-button {
    width: 100%;
    padding: 20px;
    border: 2px solid #e0e0e0;
    border-radius: 8px;
    background: white;
    cursor: pointer;
    transition: all 0.2s;
}

.auth-button:hover {
    border-color: #007bff;
    background: #f8f9fa;
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0,0,0,0.1);
}

.auth-button-content {
    display: flex;
    align-items: center;
    gap: 15px;
    text-align: left;
}

.auth-icon {
    font-size: 32px;
    flex-shrink: 0;
}

.auth-name {
    font-size: 18px;
    font-weight: 600;
    color: #333;
    display: block;
}

.auth-description {
    font-size: 14px;
    color: #666;
    display: block;
}

.auth-processing {
    text-align: center;
}

.spinner {
    margin: 20px auto;
    width: 40px;
    height: 40px;
    border: 4px solid #f3f3f3;
    border-top: 4px solid #007bff;
    border-radius: 50%;
    animation: spin 1s linear infinite;
}

@keyframes spin {
    0% { transform: rotate(0deg); }
    100% { transform: rotate(360deg); }
}
```

**Pages/Callback.razor:**
```razor
@page "/callback"
@inject AuthOrchestrator AuthOrch
@inject NavigationManager Nav

<div class="callback-container">
    <h3>Processing authentication...</h3>
    <p>@_message</p>
</div>

@code {
    private string _message = "Exchanging authorization code for tokens...";

    [SupplyParameterFromQuery]
    public string? Code { get; set; }

    [SupplyParameterFromQuery]
    public string? State { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(Code) || string.IsNullOrEmpty(State))
            {
                _message = "Invalid callback parameters";
                return;
            }

            // Parse state parameter
            var stateData = AuthOrch.ParseState(State);

            _message = $"Authenticating with {stateData.AuthConfigName}...";
            StateHasChanged();

            // Switch to correct auth context
            await AuthOrch.SelectAuthConfigAsync(stateData.AuthConfigName);

            _message = "Exchanging code for tokens...";
            StateHasChanged();

            // Exchange code for tokens
            var tokens = await AuthOrch.ExchangeCodeForTokensAsync(Code);

            _message = "Storing tokens...";
            StateHasChanged();

            // Store tokens in localStorage
            await AuthOrch.StoreTokensAsync(tokens);

            _message = "Storing subtenant...";
            StateHasChanged();

            // Store subtenant in cookie
            await AuthOrch.StoreSubtenantAsync(stateData.Subtenant);

            _message = "Redirecting to application...";
            StateHasChanged();

            // Redirect to return URL
            var returnUrl = stateData.ReturnUrl ?? "/";
            Nav.NavigateTo(returnUrl, forceLoad: true);
        }
        catch (Exception ex)
        {
            _message = $"Authentication failed: {ex.Message}";
            StateHasChanged();
        }
    }
}
```

**Services/AuthOrchestrator.cs:**
```csharp
public class AuthOrchestrator
{
    private readonly IOidcConfig _oidcConfig;
    private readonly ILzClientConfig _clientConfig;
    private readonly ISubtenantService _subtenantService;
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly TokenExchangeService _tokenExchange;
    private readonly NavigationManager _navigation;
    private readonly ILzJsUtilities _jsUtilities;

    public async Task LoadAuthConfigsAsync()
    {
        // Load from /config endpoint
        await _clientConfig.InitializeAsync(_navigation.BaseUri);
    }

    public Dictionary<string, JObject> GetAvailableAuthConfigs()
    {
        // Return all available auth configs for user selection
        return _oidcConfig.AuthConfigs;
    }

    public async Task<(bool hasValidTokens, string? authConfigName)> HasAnyValidTokensAsync()
    {
        // Check localStorage for any valid tokens from any auth config
        foreach (var authConfig in _oidcConfig.AuthConfigs)
        {
            var authConfigName = authConfig.Key;
            var config = authConfig.Value;

            var authority = config["authority"]?.ToString();
            var clientId = config["ClientId"]?.ToString();

            if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(clientId))
                continue;

            var key = $"oidc.user:{authority}:{clientId}";
            var tokenJson = await _jsUtilities.LocalStorageGetItemAsync(key);

            if (!string.IsNullOrEmpty(tokenJson))
            {
                try
                {
                    var tokenData = JsonSerializer.Deserialize<TokenData>(tokenJson);
                    if (tokenData != null && tokenData.expires_at > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    {
                        // Found valid tokens for this auth config
                        return (true, authConfigName);
                    }
                }
                catch
                {
                    // Invalid token format, skip
                }
            }
        }

        return (false, null);
    }

    public async Task SelectAuthConfigAsync(string authConfigName)
    {
        _oidcConfig.SelectedAuthConfig = authConfigName;
    }

    public async Task<bool> HasValidTokensAsync()
    {
        var tokenResult = await _tokenProvider.RequestAccessToken();
        return tokenResult.TryGetToken(out var token) &&
               !IsTokenExpired(token);
    }

    public async Task InitiateLoginAsync(string? subtenant, string? authConfigName, string? returnUrl)
    {
        // Select the auth config before building state
        await SelectAuthConfigAsync(authConfigName ?? "ConsumerAuth");

        // Build enhanced state
        var stateData = new {
            authConfigName = authConfigName ?? "ConsumerAuth",
            subtenant = subtenant,
            returnUrl = returnUrl ?? "/",
            targetDomain = await GetRootDomainAsync(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var stateJson = JsonSerializer.Serialize(stateData);
        var stateEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(stateJson));

        // Build Cognito authorize URL
        var authConfig = _oidcConfig.GetCurrentAuthConfig();
        var authorizeUrl = BuildAuthorizeUrl(authConfig, stateEncoded);

        // Navigate to Cognito
        _navigation.NavigateTo(authorizeUrl, forceLoad: true);
    }

    public async Task<AuthTokens> ExchangeCodeForTokensAsync(string code)
    {
        return await _tokenExchange.ExchangeAsync(code, _oidcConfig.GetCurrentAuthConfig());
    }

    public async Task StoreTokensAsync(AuthTokens tokens)
    {
        // Store in localStorage (Microsoft OIDC format)
        var authConfig = _oidcConfig.GetCurrentAuthConfig();
        var authority = authConfig["authority"]?.ToString();
        var clientId = authConfig["ClientId"]?.ToString();
        var key = $"oidc.user:{authority}:{clientId}";

        await _jsUtilities.LocalStorageSetItemAsync(key, JsonSerializer.Serialize(tokens));
    }

    public async Task StoreSubtenantAsync(string? subtenant)
    {
        if (!string.IsNullOrEmpty(subtenant))
        {
            await _subtenantService.SetSubtenantAsync(subtenant);
        }
    }

    public StateData ParseState(string stateEncoded)
    {
        var stateJson = Encoding.UTF8.GetString(Convert.FromBase64String(stateEncoded));
        return JsonSerializer.Deserialize<StateData>(stateJson);
    }
}
```

---

### Phase 4: Test App Updates (BlazorTest.WASM)

#### 2.7 Update BlazorTest.WASM

**Modify:** `Program.cs`
```csharp
// Add subtenant service
builder.Services.AddScoped<ISubtenantService, SubtenantService>();

// Configure HttpClient with message handlers
builder.Services.AddHttpClient("LazyMagicAPI", client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
})
.AddHttpMessageHandler<AuthorizationMessageHandler>()
.AddHttpMessageHandler<SubtenantMessageHandler>();  // NEW!

// Register SubtenantMessageHandler
builder.Services.AddTransient<SubtenantMessageHandler>();
```

**New Component:** `Components/SubtenantDisplay.razor`
```razor
@inject ISubtenantService SubtenantService

<div class="subtenant-info">
    <strong>Current Subtenant:</strong> @_currentSubtenant
</div>

@code {
    private string? _currentSubtenant;

    protected override async Task OnInitializedAsync()
    {
        _currentSubtenant = await SubtenantService.GetSubtenantAsync();
    }
}
```

---

### Phase 5: CloudFront & Infrastructure

#### 5.1 Existing CloudFront Request Function Modifications

**File:** `/mnt/c/Users/TimothyMay/repos/_Dev/MagicPets/Service/AWSTemplates/Templates/sam.policies.yaml`

**Current Architecture:**
- RequestFunction (lines 116-322): ~5.5KB current size
- Handles asset routing via KVS config
- Already uses GetConfig() to fetch configuration from KVS
- Already adds headers: `lz-config`, `lz-tenantid`

**CODE QUALITY REQUIREMENTS:**
- ✅ **NO single-letter variable names** - use descriptive names (e.g., `domainParts`, `subtenant`, `cookieHeader`)
- ✅ **PRESERVE all existing debug logging** - do not remove any `logMessage` or `console.log` statements
- ✅ **ADD debug logging for new features** - new code should follow existing debug patterns
- ✅ **DO NOT modify existing code** - only ADD new code sections marked with `// AUTH GEN2` comments
- ✅ **Stay under 10KB limit** - current ~5.5KB + additions ~300 bytes = ~5.8KB total

**Required Modifications:**

**A. Modify GetConfig() to Support Subtenant Cookie**

The existing `GetConfig(host)` function needs to be enhanced to construct the KVS key from the lz-subtenant cookie + host header.

Insert BEFORE the existing `GetConfig()` call (around line 192):

```javascript
// AUTH GEN2: Construct KVS lookup key from subtenant cookie + host
let kvsLookupKey = originalDomain; // Default to full domain from host header

const cookies = headers.cookie?.[0]?.value || '';
const subtenantMatch = cookies.match(/lz-subtenant=([^;]+)/);
if (subtenantMatch) {
    const subtenantFromCookie = subtenantMatch[1];
    const rootDomain = originalDomain.split('.').slice(-2).join('.');
    kvsLookupKey = subtenantFromCookie + '.' + rootDomain;

    if (debug) {
        logMessage += '\nAUTH GEN2 KVS LOOKUP:'
                   + '\n  subtenant from cookie: ' + subtenantFromCookie
                   + '\n  root domain: ' + rootDomain
                   + '\n  KVS key: ' + kvsLookupKey;
    }
}
// END AUTH GEN2

// Get config from KVS using constructed key
let config = await GetConfig(kvsLookupKey);
```

**B. Add Auth Redirect Logic**

Insert after line 150 (after the existing querystring debug logging block, before the `err` helper function):

```javascript
// AUTH GEN2: Handle subdomain-to-auth redirection
// This logic redirects requests from subdomains to the root domain auth page
// when the lz-subtenant cookie is not present (first-time access from subdomain)
const domainParts = originalDomain.split('.');
if (domainParts.length > 2 && originalUri !== '/auth' && !originalUri.startsWith('/auth/') && !originalUri.startsWith('/callback')) {
    const cookies = headers.cookie?.[0]?.value || '';
    if (!cookies.includes('lz-subtenant=')) {
        const subtenant = domainParts[0];
        const rootDomain = domainParts.slice(-2).join('.');
        const redirectLocation = `https://${rootDomain}/auth?subtenant=${subtenant}&returnUrl=${encodeURIComponent(originalUri)}`;

        // Add to debug logging
        if (debug) {
            logMessage += '\nAUTH GEN2 REDIRECT:'
                       + '\n  subdomain detected: ' + subtenant
                       + '\n  no lz-subtenant cookie found'
                       + '\n  redirecting to: ' + redirectLocation;
            console.log(logMessage);
        }

        // Redirect to auth page (user will select auth method)
        return {
            statusCode: 302,
            statusDescription: 'Found',
            headers: {
                'location': {value: redirectLocation},
                'cache-control': {value: 'no-store'}
            }
        };
    } else if (debug) {
        // Debug: Cookie found, no redirect needed
        logMessage += '\nAUTH GEN2: lz-subtenant cookie present, continuing normally';
    }
}
// END AUTH GEN2
```

**Important Notes:**
- Preserves ALL existing debug logging structure
- Uses descriptive variable names (no single-letter variables)
- Adds new debug output when redirect occurs
- Insert AFTER existing querystring logging (line 150), BEFORE `err` helper (line 153)
- Does NOT modify any existing code, only adds new logic

**Note:** This simplified redirect does NOT include `tenantauth` parameter. The auth app will present available auth options to the user for selection.

**B. Add Subtenant Header from Cookie**

In the "api" case (around line 215-228), ADD these lines AFTER line 220 (`headers['lz-tenantid'] = ...`):

```javascript
// Existing code (DO NOT MODIFY):
// headers['lz-config'] = {value: configJson};
// headers['lz-tenantid'] = {value: headers.host.value};

// NEW AUTH GEN2: Add authname header if present in config
if (config.authname) {
    headers['lz-authname'] = {value: config.authname};
}

// NEW AUTH GEN2: Pass subtenant from cookie to API backend
const cookieHeader = headers.cookie?.[0]?.value || '';
const subtenantMatch = cookieHeader.match(/lz-subtenant=([^;]+)/);
if (subtenantMatch) {
    const subtenantFromCookie = subtenantMatch[1];
    headers['lz-subtenant'] = {value: subtenantFromCookie};

    // Add to debug logging
    if (debug) {
        logMessage += '\nAUTH GEN2: Extracted subtenant from cookie: ' + subtenantFromCookie;
    }
}
// END AUTH GEN2

// Existing code continues (authheader check, etc.)
```

**Important Notes:**
- Insert AFTER `headers['lz-tenantid']` line (line 220)
- Insert BEFORE the existing `authheader` check (line 221)
- Uses descriptive variable names: `cookieHeader`, `subtenantMatch`, `subtenantFromCookie`
- Adds debug logging for subtenant extraction
- Does NOT modify any existing header assignments

**C. Update Cache Policies**

Modify `CacheByHeaderDevPolicy` and `CacheByHeaderProdPolicy` (lines 347-387):

Change from:
```yaml
CookiesConfig:
  CookieBehavior: none
```

To:
```yaml
CookiesConfig:
  CookieBehavior: whitelist
  Cookies:
    - lz-subtenant
```

**D. KVS Schema - No Changes Required**

The KVS schema does NOT need a `tenantauth` field since users select their auth method in the auth app UI. The CloudFront function only needs to extract the subtenant and redirect to `/auth`.

Example KVS entry (unchanged from current schema):

```json
// Root domain entry - no tenantauth needed
Key: "lazymagicdev.click"
Value: {
  "env": "dev",
  "systemKey": "MagicPets",
  "tenantKey": "magicpets",
  "ss": "",
  "ts": "",
  "region": "us-east-1",
  "more": null
}

// Subtenant entries (existing schema)
Key: "uptown.lazymagicdev.click"
Value: {
  ... subtenant config ...
}
```

**Size Impact Analysis:**
- Current RequestFunction: ~5,500 bytes
- Auth redirect logic (simplified, no KVS lookup): ~200 bytes
- Debug logging for redirect: ~150 bytes
- Cookie parsing and subtenant header: ~100 bytes
- Debug logging for cookie extraction: ~80 bytes
- **New total: ~6,030 bytes**
- **Well under 10KB limit (10,240 bytes) - 58% capacity used**
- **Bonus:** Removed KVS lookup reduces CloudFront function execution time!

**Benefits of Modifying Existing Function:**
- Reuses existing GetConfig() infrastructure
- Maintains all current functionality
- Single function to maintain and debug
- No additional CloudFront behavior configuration needed
- Minimal size increase

**Testing:**
- Test subdomain redirect with no cookie → should redirect to /auth
- Test root domain access with cookie → should route normally
- Test /auth and /callback paths → should NOT trigger redirect
- Test backward compatibility → existing apps continue working
- Verify cache policy includes lz-subtenant cookie

---

## 3. Configuration Updates

### 3.1 Auth Config (wwwroot/config)

```json
{
  "meta": {
    "tenantKey": "magicpets",
    "wsUrl": "wss://api.lazymagicdev.click/events"
  },
  "authConfigs": {
    "ConsumerAuth": {
      "HostedUIDomain": "https://consumer.auth.us-east-1.amazoncognito.com",
      "MetadataUrl": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ABC123/.well-known/openid-configuration",
      "ClientId": "abc123clientid",
      "authority": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_ABC123",
      "useCallbackProxy": false
    },
    "AdminAuth": {
      "HostedUIDomain": "https://admin.auth.us-east-1.amazoncognito.com",
      "MetadataUrl": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_XYZ789/.well-known/openid-configuration",
      "ClientId": "xyz789clientid",
      "authority": "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_XYZ789",
      "useCallbackProxy": false
    }
  }
}
```

### 3.2 Cognito User Pool Configuration

**For ALL auth configs:**
- Callback URL: `https://lazymagicdev.click/callback`
- Logout URL: `https://lazymagicdev.click/logout`
- **Total URLs per pool: 2** (well under 100 limit)

---

## 4. File Summary

### New Files to Create:

**LazyMagic Libraries:**
1. `LazyMagic.Client.Base/Services/ISubtenantService.cs`
2. `LazyMagic.Client.Base/Services/SubtenantService.cs`
3. `LazyMagic.Client.Base/Handlers/SubtenantMessageHandler.cs`
4. `LazyMagic.Client.Base/Models/CookieOptions.cs`

**Auth App (Entire Project):**
5. `LazyMagic.Auth.WASM/LazyMagic.Auth.WASM.csproj`
6. `LazyMagic.Auth.WASM/Program.cs`
7. `LazyMagic.Auth.WASM/App.razor`
8. `LazyMagic.Auth.WASM/Pages/Index.razor`
9. `LazyMagic.Auth.WASM/Pages/Callback.razor`
10. `LazyMagic.Auth.WASM/Pages/Logout.razor`
11. `LazyMagic.Auth.WASM/Services/AuthOrchestrator.cs`
12. `LazyMagic.Auth.WASM/Services/TokenExchangeService.cs`
13. `LazyMagic.Auth.WASM/Models/AuthFlowState.cs`
14. `LazyMagic.Auth.WASM/wwwroot/index.html`

**Test App:**
15. `BlazorTest.WASM/Components/SubtenantDisplay.razor`

### Files to Modify:

**LazyMagic Libraries:**
1. `LazyMagic.Service.Authorization/LzAuthorization.cs` (AddConfigAsync method)
2. `LazyMagic.Client.Base/Utilities/LzJsUtilities.cs` (Add cookie wrappers)
3. `LazyMagic.OIDC.WASM/OIDC/BlazorOIDCService.cs` (Enhanced state parameter)

**Test App:**
4. `BlazorTest.WASM/Program.cs` (Register services, configure HttpClient)

**CloudFront:**
5. `/mnt/c/Users/TimothyMay/repos/_Dev/MagicPets/Service/AWSTemplates/Templates/sam.policies.yaml`
   - RequestFunction FunctionCode (add auth redirect + cookie handling)
   - CacheByHeaderDevPolicy (add cookie to whitelist)
   - CacheByHeaderProdPolicy (add cookie to whitelist)

---

## 5. Testing Strategy

### 5.1 Unit Tests
- SubtenantService: Get/set/clear subtenant
- SubtenantMessageHandler: Header injection
- LzAuthorization: Cookie/header reading
- AuthOrchestrator: State parameter encoding/decoding
- CloudFront function: Redirect logic, cookie parsing

### 5.2 Integration Tests
- Complete auth flow: Login → Callback → App load
- Token storage: localStorage format validation
- Cookie storage: Cross-subdomain accessibility
- Multi-auth: Switch between ConsumerAuth/AdminAuth
- CloudFront routing: Subdomain to auth redirect

### 5.3 End-to-End Tests
1. **First Visit Flow:**
   - Access `uptown.lazymagicdev.click/store`
   - CloudFront redirects to `/auth`
   - Login via Cognito
   - Callback processes tokens
   - App loads at root domain
   - Subtenant cookie set
   - API calls include subtenant header

2. **Return Visit Flow:**
   - Access `lazymagicdev.click/store` directly
   - Tokens found in localStorage
   - Subtenant found in cookie
   - App loads immediately

3. **Subtenant Switch Flow:**
   - Change subtenant cookie
   - CloudFront routes to different origin
   - API requests use new subtenant

4. **Multi-Auth Flow:**
   - Login with ConsumerAuth
   - Switch to admin section
   - Redirect to `/auth?tenantauth=AdminAuth`
   - Login with admin credentials
   - Both tokens coexist

5. **CloudFront Size Verification:**
   - Measure deployed function size
   - Verify < 10KB limit
   - Test with various request patterns

---

## 6. Migration & Rollout

### 6.1 Phase A: Infrastructure (Non-Breaking)
- Deploy LazyMagic library updates
- Add SubtenantService, message handlers
- Enhance LzAuthorization
- **No breaking changes**

### 6.2 Phase B: Auth App
- Deploy LazyMagic.Auth.WASM to `/auth`
- Configure Cognito callback URLs
- Test auth flow in isolation

### 6.3 Phase C: CloudFront
- Update sam.policies.yaml RequestFunction
- Update cache policies
- Deploy CloudFormation stack
- Update KVS entries with tenantauth field
- Test routing logic

### 6.4 Phase D: Test App
- Update BlazorTest.WASM
- End-to-end testing
- Performance validation

### 6.5 Phase E: Production
- Migrate production apps
- Monitor metrics
- Gradual rollout per subtenant

---

## 7. Success Criteria

✅ **Functional Requirements:**
- User can access via subdomain (uptown.lazymagicdev.click)
- Automatic redirect to /auth
- Tokens stored in localStorage (Microsoft compatible)
- Subtenant stored in cookie (CloudFront accessible)
- Apps run at root domain
- Multiple auth configs work independently
- CloudFront routes correctly per subtenant

✅ **Non-Functional Requirements:**
- No Cognito callback URL limit issues (2 URLs per pool vs 100 limit)
- Unlimited subdomain support
- Fast auth check (<100ms with FastAuth)
- No CSRF vulnerabilities (SameSite=Lax, headers require explicit code)
- Backward compatible with existing apps
- CloudFront function stays under 10KB limit

✅ **Performance Targets:**
- First auth: <2s total (redirect + Cognito + callback)
- Cached auth: <100ms (localStorage + cookie read)
- API latency overhead: <5ms (header injection)
- CloudFront redirect overhead: <50ms

---

## 8. CloudFront Function Size Budget

**Current:**
- RequestFunction: ~5,500 bytes
- AuthConfigFunction: ~2,000 bytes

**After Changes:**
- RequestFunction: ~5,850 bytes (+350 bytes)
- AuthConfigFunction: ~2,000 bytes (unchanged)

**Remaining Budget:**
- RequestFunction: 4,390 bytes available
- Total 10KB limit: 4,240 bytes used, 5,760 bytes remaining

**Size Optimization Notes:**
- Use single-letter variable names in production
- Remove debug logging
- Minify before deployment
- Reuse existing GetConfig() function
- Avoid adding new KVS lookups

---

This plan leverages existing LazyMagic infrastructure (ICallerInfo, LzAuthorization, OIDC services) and existing CloudFront RequestFunction to achieve domain-level multi-auth with unlimited subdomain support while staying well under the 10KB CloudFront function size limit.
