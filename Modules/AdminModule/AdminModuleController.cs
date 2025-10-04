namespace AdminModule;

[ApiController]
public partial class AdminModuleController
{
    // We implement our own constructcor to inject the ISubtenantRepo
    // as SubtenantRepo is not generated. You must update this constructor
    // if a new generated repo is added/delted on the module. Just compare
    // the generated code constructor with this constructor to see what
    // needs to be updated.

    // We extend the constructor to include those repos that are not found 
    // as transitive dependencies of this moudle. Transitive dependencies are
    // found by path references to schemas. We have methods in this module 
    // that use repos that are not found by path references.

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
    public IPetRepo PetRepo { get; set; }
    public ITagRepo TagRepo { get; set; }
    public ICategoryRepo CategoryRepo { get; set; }
    public IOrderRepo OrderRepo { get; set; }

    // Implement methods for which the generator does not generate 
    // an implementation. 

    public override async Task<ActionResult<TenantUserStatus>> AdminModuleIsAdminAsync()
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

    [HttpGet, Route("AdminModule/subtenant/seedPets/{store}/{numPets}")]
    public override async Task<IActionResult> AdminModuleSeedPetsAsync(int numPets, string store)
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
    public override async Task<ActionResult<ICollection<Subtenant>>> AdminModuleListSubtenantsAsync()
    {
        var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
        return await SubtenantRepo.ListAsync(callerInfo);
    }
}
