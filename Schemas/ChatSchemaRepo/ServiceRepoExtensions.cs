namespace ChatSchemaRepo;

public static partial class ChatSchemaRepoExtensions
{
    static partial void AddCustom(IServiceCollection services)
    {
        // Register AWS services needed by ChatManager
        services.TryAddAWSService<IAmazonBedrockRuntime>();
        services.TryAddAWSService<IAmazonAppSync>();

        // Register HTTP client factory for keep-alive requests
        services.AddHttpClient("KeepAlive", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(15); // Long timeout for keep-alive requests
        });

        // Register LLM client (default: BedrockChat, can be swapped with other implementations)
        services.TryAddSingleton<BedrockChat>();
        services.TryAddSingleton<ILlmClient>(sp => sp.GetRequiredService<BedrockChat>());

        // Register chat business logic services
        services.TryAddSingleton<AppSyncEventPublisher>();

        // Register IMessagePersistence as singleton wrapper that creates transient ChatMessagesRepo instances
        services.AddSingleton<IMessagePersistence>(sp => new MessagePersistenceWrapper(sp));

        // Register ChatManagerService as both IChatManagerService and itself
        services.TryAddSingleton<ChatManagerService>();
        services.TryAddSingleton<IChatManagerService>(sp => sp.GetRequiredService<ChatManagerService>());

        // Register ChatManagerService as IHostedService for background processing
        services.AddHostedService(sp => sp.GetRequiredService<ChatManagerService>());
    }
}
