namespace ChatModule;

public static partial class ChatModuleRegistrations
{
    static partial void CustomConfigurations(IServiceCollection sdervices)
    {
        // All service registrations are handled by ChatSchemaRepo/ServiceRepoExtensions.cs
        // which is called via AddChatSchemaRepo() in ChatModuleRegistrations.g.cs
    }
}