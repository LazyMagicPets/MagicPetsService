namespace AdminModule;

[ApiController]
public partial class AdminModuleController
{
    // We implement our own constructcor to inject the ISubtenantRepo
    // as SubtenantRepo is not generated. You must update this constructor
    // if a new generated repo is added/delted on the module. Just compare
    // the generated code constructor with this constructor to see what
    // needs to be updated.

    [ActivatorUtilitiesConstructor] // force DI to use this constructor
    public AdminModuleController(
        IAdminModuleAuthorization adminModuleAuthorization,
        ICategoryRepo categoryRepo,
        ITagRepo tagRepo,
        IPetRepo petRepo,
        IOrderRepo orderRepo,
        ITenantUserRepo tenantUserRepo,
        ISubtenantRepo subtenantRepo
        ) 
    {
        AdminModuleAuthorization = adminModuleAuthorization;
        CategoryRepo = categoryRepo;
        TagRepo = tagRepo;
        PetRepo = petRepo;
        OrderRepo = orderRepo;
        TenantUserRepo = tenantUserRepo;
        SubtenantRepo = subtenantRepo;

        Init();
    }

    public ISubtenantRepo SubtenantRepo { get; set; }

    // Implement methods for which the generator does not generate 
    // an implementation. 

    public override async Task<ActionResult<TenantUserStatus>> AdminModuleIsAdmin()
    {
        try
        {
            // callerInfo will throw if permission is denied
            Console.WriteLine("Checking if user is admin");
            var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
            return new TenantUserStatus() { IsAdmin = true };
        }
        catch
        {
            return new TenantUserStatus() { IsAdmin = false };
        }
    }

    public override async Task<IActionResult> AdminModuleSeedPets(string store, int numPets)
    {
        try
        {
            Console.WriteLine($"Seeding {numPets} pets in store {store}");
            var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
            // Since we may be in a different tenancy, usually the main tenant, when this 
            // is called, we find the Subtenant record for the store.
            var subtenantResult = await SubtenantRepo.ReadAsync(callerInfo, store);
            if (subtenantResult == null)
            {
                return NotFound();
            }
            var subtenant = subtenantResult.Value!;
            subtenant.SetCalculatedFields();
            callerInfo.DefaultDB = subtenant.DefaultDB;
            return await PetRepo.SeedAsync(callerInfo, numPets);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
    }

    [HttpGet, Route("AdminModule/subtenant/listSubtenants")]
    public override async Task<ActionResult<ICollection<Subtenant>>> AdminModuleListSubtenants()
    {
        var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
        return await SubtenantRepo.ListAsync(callerInfo);
    }
}
