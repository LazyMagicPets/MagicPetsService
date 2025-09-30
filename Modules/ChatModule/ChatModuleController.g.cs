
namespace ChatModule;
public partial class ChatModuleController : ChatModuleControllerBase {
        public ChatModuleController(
            IChatModuleAuthorization chatModuleAuthorization,
			ICategoryRepo categoryRepo,
			ITagRepo tagRepo,
			IPetRepo petRepo,
			IOrderRepo orderRepo
            ) 
        {
            ChatModuleAuthorization = chatModuleAuthorization;
			CategoryRepo = categoryRepo;
			TagRepo = tagRepo;
			PetRepo = petRepo;
			OrderRepo = orderRepo;

            Init();
        }
}
