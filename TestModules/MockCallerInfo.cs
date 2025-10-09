using LazyMagic;

namespace TestModules;

/// <summary>
/// Mock implementation of ICallerInfo for testing purposes.
/// Configured to use the lzm_mp_uptown TenantDB without authentication.
/// </summary>
public class MockCallerInfo : ICallerInfo
{
    public string? TenantId { get; set; } = "mp";
    public string? SessionId { get; set; } = "test-session";
    public string? LzUserId { get; set; } = "test-user";
    public string? UserId { get; set; } = "test-user";
    public string? UserName { get; set; } = "Test User";
    public string? UserEmail { get; set; } = "test@example.com";

    // System/Tenant/Subtenant identifiers
    public string? System { get; set; } = "lzm";
    public string? Tenant { get; set; } = "mp";
    public string? Subtenant { get; set; } = "uptown";

    // Database names
    public string? SystemDB { get; set; } = "lzm";
    public string? TenantDB { get; set; } = "lzm_mp";
    public string? SubtenantDB { get; set; } = "lzm_mp_uptown";

    // Asset paths
    public string? SystemAssets { get; set; } = "lzm";
    public string? TenantAssets { get; set; } = "mp";
    public string? SubtenantAssets { get; set; } = "uptown";

    // Defaults
    public string? DefaultTenant { get; set; } = "mp";
    public string? DefaultDB { get; set; } = "lzm_mp_uptown";
    public string? DefaultAssets { get; set; } = "uptown";

    // Collections
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, string> Claims { get; set; } = new();
    public List<string> Permissions { get; set; } = new();

    public MockCallerInfo()
    {
        // Initialize with common test headers
        Headers["Host"] = "localhost:5001";
        Headers["Content-Type"] = "application/json";

        // Initialize with common test claims
        if (UserId != null && UserEmail != null && UserName != null)
        {
            Claims["sub"] = UserId;
            Claims["email"] = UserEmail;
            Claims["name"] = UserName;
        }

        // Initialize with test permissions
        Permissions.Add("read");
        Permissions.Add("write");
        Permissions.Add("delete");
    }
}
