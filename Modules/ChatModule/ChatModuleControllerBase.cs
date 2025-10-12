namespace ChatModule;

/// <summary>
/// Custom partial class to add properties for services not known to the code generator.
/// This file will not be overwritten by code generation.
/// </summary>
public abstract partial class ChatModuleControllerBase
{
    // ChatManagerService is used by generated methods that call ChatManagerService.CreateChatAsync, etc.
    public IChatManagerService ChatManagerService { get; set; } = null!;
}
