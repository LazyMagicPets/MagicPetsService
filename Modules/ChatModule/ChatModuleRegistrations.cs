using Amazon.AppSync;
using Amazon.BedrockRuntime;

namespace ChatModule;

public static partial class ChatModuleRegistrations
{
    static partial void CustomConfigurations(IServiceCollection sdervices)
    {
        // Register AWS services
        sdervices.AddAWSService<IAmazonAppSync>();
        sdervices.AddAWSService<IAmazonBedrockRuntime>();

        // Register custom services
        sdervices.AddSingleton<AppSyncEventPublisher>();
        sdervices.AddScoped<BedrockChat>();
    }
}