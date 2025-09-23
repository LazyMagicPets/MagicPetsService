
namespace AdminModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public interface IAdminModuleController
    {

        /// <summary>
        /// Check if currently logged in tenantUser is an admin
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUserStatus>> AdminModuleIsAdminAsync();

        /// <summary>
        /// Add a new tenantUser
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUser>> AdminModuleAddTenantUserAsync(TenantUser body = null);

        /// <summary>
        /// Update an existing tenantUser
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUser>> AdminModuleUpdateTenantUserAsync(TenantUser body = null);

        /// <summary>
        /// List all tenantUsers
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<TenantUser>>> AdminModuleListTenantUsersAsync();

        /// <summary>
        /// Add a new Subtenant
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Subtenant>> AdminModuleAddSubtenantAsync(Subtenant body = null);

        /// <summary>
        /// Update an existing Subtenant
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Subtenant>> AdminModuleUpdateSubtenantAsync(Subtenant body = null);

        /// <summary>
        /// List all Subtenants
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Subtenant>>> AdminModuleListSubtenantsAsync();

        /// <summary>
        /// See pet database
        /// </summary>

        /// <param name="numPets">Number of pets to seed</param>

        /// <param name="store">Store to seed</param>

        /// <returns>Success</returns>

        Task<IActionResult> AdminModuleSeedPetsAsync(int numPets, string store);

        /// <summary>
        /// Suspend TenantUser
        /// </summary>

        /// <param name="tenantUser">tenantUser login</param>

        /// <returns>Success</returns>

        Task<IActionResult> AdminModuleSuspendTenantUserAsync(string tenantUser);

        /// <summary>
        /// Find tenantUser by ID
        /// </summary>

        /// <param name="tenantUserId">ID of tenantUser that needs to be fetched</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<TenantUser>> AdminModuleGetTenantUserByIdAsync(string tenantUserId);

        /// <summary>
        /// Delete tenantUser by ID
        /// </summary>

        /// <param name="tenantUserId">ID of tenantUser that needs to be deleted</param>

        /// <returns>Success</returns>

        Task<IActionResult> AdminModuleDeleteTenantUserAsync(string tenantUserId);

        /// <summary>
        /// Find Subtenant by ID
        /// </summary>

        /// <param name="subtenantId">ID of Subtenant that needs to be fetched</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Subtenant>> AdminModuleGetSubtenantByIdAsync(string subtenantId);

        /// <summary>
        /// Delete Subtenant by ID
        /// </summary>

        /// <param name="subtenantId">ID of Subtenant that needs to be deleted</param>

        /// <returns>Success</returns>

        Task<IActionResult> AdminModuleDeleteSubtenantAsync(string subtenantId);

    }

}
