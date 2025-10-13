using Microsoft.Extensions.DependencyInjection;

namespace ChatModule;

/// <summary>
/// Custom partial class to inject dependencies not included in generated code.
/// This file will not be overwritten by code generation.
/// </summary>
public partial class ChatModuleController : ChatModuleControllerBase
{
    [ActivatorUtilitiesConstructor]
    public ChatModuleController(
        IChatModuleAuthorization chatModuleAuthorization,
        IChatRepo chatRepo,
        IChatContextRepo chatContextRepo,
        IChatManagerService chatManagerService)
    {
        ChatModuleAuthorization = chatModuleAuthorization;
        ChatRepo = chatRepo;
        ChatContextRepo = chatContextRepo;
        ChatManagerService = chatManagerService;

        Init();
    }
}
