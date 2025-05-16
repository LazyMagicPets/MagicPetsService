
namespace AdminModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public interface IAdminModuleController
    {

        /// <summary>
        /// Check if currently logged in tenantUser is an admin
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUserStatus>> IsAdmin();

        /// <summary>
        /// Add a new tenantUser
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUser>> AddTenantUser(TenantUser body);

        /// <summary>
        /// Update an existing tenantUser
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUser>> UpdateTenantUser(TenantUser body);

        /// <summary>
        /// List all tenantUsers
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<TenantUser>>> ListTenantUsers();

        /// <summary>
        /// Add a new Subtenant
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Subtenant>> AddSubtenant(Subtenant body);

        /// <summary>
        /// Update an existing Subtenant
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Subtenant>> UpdateSubtenant(Subtenant body);

        /// <summary>
        /// List all Subtenants
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Subtenant>>> ListSubtenants();

        /// <summary>
        /// See pet database
        /// </summary>

        /// <param name="store">Store to seed</param>

        /// <param name="numPets">Number of pets to seed</param>

        /// <returns>Success</returns>

        Task<IActionResult> SeedPets(string store, int numPets);

        /// <summary>
        /// Suspend TenantUser
        /// </summary>

        /// <param name="tenantUser">tenantUser login</param>

        /// <returns>Success</returns>

        Task<IActionResult> SuspendTenantUser(string tenantUser);

        /// <summary>
        /// Find tenantUser by ID
        /// </summary>

        /// <param name="tenantUserId">ID of tenantUser that needs to be fetched</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUser>> GetTenantUserById(string tenantUserId);

        /// <summary>
        /// Delete tenantUser by ID
        /// </summary>

        /// <param name="tenantUserId">ID of tenantUser that needs to be deleted</param>

        /// <returns>Success</returns>

        Task<IActionResult> DeleteTenantUser(string tenantUserId);

        /// <summary>
        /// Find Subtenant by ID
        /// </summary>

        /// <param name="subtenantId">ID of Subtenant that needs to be fetched</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Subtenant>> GetSubtenantById(string subtenantId);

        /// <summary>
        /// Delete Subtenant by ID
        /// </summary>

        /// <param name="subtenantId">ID of Subtenant that needs to be deleted</param>

        /// <returns>Success</returns>

        Task<IActionResult> DeleteSubtenant(string subtenantId);

    }

}
