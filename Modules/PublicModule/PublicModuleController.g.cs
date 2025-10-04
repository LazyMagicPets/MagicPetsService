
namespace PublicModule;
public partial class PublicModuleController : PublicModuleControllerBase {
        public PublicModuleController(
            IPublicModuleAuthorization publicModuleAuthorization,
			ICategoryRepo categoryRepo,
			ITagRepo tagRepo,
			IPetRepo petRepo,
			IBadaRepo badaRepo,
			IFingerprintRepo fingerprintRepo
            ) 
        {
            PublicModuleAuthorization = publicModuleAuthorization;
			CategoryRepo = categoryRepo;
			TagRepo = tagRepo;
			PetRepo = petRepo;
			BadaRepo = badaRepo;
			FingerprintRepo = fingerprintRepo;

            Init();
        }
}
