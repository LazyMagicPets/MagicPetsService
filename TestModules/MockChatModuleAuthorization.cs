using ChatModule;
using LazyMagic;
using Microsoft.AspNetCore.Http;

namespace TestModules;

/// <summary>
/// Mock authorization implementation for testing that bypasses HTTP request validation
/// and returns a pre-configured MockCallerInfo.
/// </summary>
public class MockChatModuleAuthorization : IChatModuleAuthorization
{
    private readonly ICallerInfo _callerInfo;

    public MockChatModuleAuthorization(ICallerInfo callerInfo)
    {
        _callerInfo = callerInfo;
    }

    public Task<ICallerInfo> GetCallerInfoAsync(HttpRequest request)
    {
        // Return the mock caller info, ignoring the request
        return Task.FromResult(_callerInfo);
    }

    public Task<ICallerInfo> GetCallerInfoAsync(HttpRequest request, string authorizationHeader)
    {
        // Return the mock caller info, ignoring the request and authorization header
        return Task.FromResult(_callerInfo);
    }
}
