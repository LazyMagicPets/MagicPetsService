
namespace ChatModule;
public partial class ChatModuleController : ChatModuleControllerBase {
        public ChatModuleController(
            IChatModuleAuthorization chatModuleAuthorization,
			IChatRepo chatRepo,
			IChatMessagesRepo chatMessagesRepo
            ) 
        {
            ChatModuleAuthorization = chatModuleAuthorization;
			ChatRepo = chatRepo;
			ChatMessagesRepo = chatMessagesRepo;

            Init();
        }
}
