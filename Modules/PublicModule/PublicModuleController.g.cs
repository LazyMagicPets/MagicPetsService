
namespace PublicModule;
public partial class PublicModuleController : PublicModuleControllerBase {
        public PublicModuleController(
            IPublicModuleAuthorization publicModuleAuthorization,
			ICategoryRepo categoryRepo,
			ITagRepo tagRepo,
			IPetRepo petRepo,
			IOrderRepo orderRepo,
			IBadaRepo badaRepo,
			IFingerprintRepo fingerprintRepo
            ) 
        {
            PublicModuleAuthorization = publicModuleAuthorization;
			CategoryRepo = categoryRepo;
			TagRepo = tagRepo;
			PetRepo = petRepo;
			OrderRepo = orderRepo;
			BadaRepo = badaRepo;
			FingerprintRepo = fingerprintRepo;

            Init();
        }
}
