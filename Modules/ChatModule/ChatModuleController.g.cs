
namespace ChatModule;
public partial class ChatModuleController : ChatModuleControllerBase {
        public ChatModuleController(
            IChatModuleAuthorization chatModuleAuthorization,
			IChatRepo chatRepo,
			IChatContextRepo chatContextRepo
            ) 
        {
            ChatModuleAuthorization = chatModuleAuthorization;
			ChatRepo = chatRepo;
			ChatContextRepo = chatContextRepo;

            Init();
        }
}
