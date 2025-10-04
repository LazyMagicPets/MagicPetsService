
namespace AdminModule;
public partial class AdminModuleController : AdminModuleControllerBase {
        public AdminModuleController(
            IAdminModuleAuthorization adminModuleAuthorization,
			ITenantUserRepo tenantUserRepo
            ) 
        {
            AdminModuleAuthorization = adminModuleAuthorization;
			TenantUserRepo = tenantUserRepo;

            Init();
        }
}
