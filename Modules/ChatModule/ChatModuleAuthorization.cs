namespace ChatModule;

public partial class ChatModuleAuthorization
{
    protected override Dictionary<string, string> GetLzHeaders(HttpRequest request)
    {
        // Get base lz-* headers
        var headers = base.GetLzHeaders(request);

        // Add Host header for keep-alive requests
        // This is used by ChatManagerService to construct the service URL for background keep-alive requests
        headers["Host"] = request.Host.ToString();

        return headers;
    }

    public override async Task<bool> HasPermissionAsync(string methodName, List<string> userPermissions)
    {
        await Task.Delay(0);
        return true;
    }
}
