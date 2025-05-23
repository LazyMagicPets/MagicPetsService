// NSWAG code refactored by LazyMagic.
// We use NSWAG to generate a baseclass, partial class and interface. 
// I{ModuleName}Controller.g.cs - defines a partial interface
// {ModuleName}ControllerBase.g.cs - defines the base class
// {ModuleName}Controller.g.cs - defines a partial class that inherits the base class
// To add or override class behavior, create a new partial class file
// {projectName}Controller.cs - overrides methods in the base class
// Dependency Injection system.
// Note: We also generate some helper classes 
// {ModuleName}Authorization.g.cs - Partial class for Authorization system
// {ModuleName}Registration.g.cs - Registers classes with the DI system
//
//----------------------
// <auto-generated>
//     Generated using the NSwag toolchain v14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0)) (http://NSwag.org)
// </auto-generated>
//----------------------

namespace AdminModule
{
    using System = global::System;

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0))")]

    public abstract class AdminModuleControllerBase : Controller, IAdminModuleController
    {

        /// <summary>
        /// Check if currently logged in tenantUser is an admin
        /// </summary>
        /// <returns>successful operation</returns>
        [HttpGet, Route("isAdmin")]
        public virtual async Task<ActionResult<TenantUserStatus>> IsAdmin()
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Add a new tenantUser
        /// </summary>
        /// <returns>successful operation</returns>
        [HttpPost, Route("tenantUser")]
        public virtual async Task<ActionResult<TenantUser>> AddTenantUser([FromBody] TenantUser body)
        {
            var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await TenantUserRepo.CreateAsync(callerInfo, body);
        }
        /// <summary>
        /// Update an existing tenantUser
        /// </summary>
        /// <returns>successful operation</returns>
        [HttpPut, Route("tenantUser")]
        public virtual async Task<ActionResult<TenantUser>> UpdateTenantUser([FromBody] TenantUser body)
        {
            var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await TenantUserRepo.UpdateAsync(callerInfo, body);
        }
        /// <summary>
        /// List all tenantUsers
        /// </summary>
        /// <returns>successful operation</returns>
        [HttpGet, Route("tenantUser/listTenantUsers")]
        public virtual async Task<ActionResult<System.Collections.Generic.ICollection<TenantUser>>> ListTenantUsers()
        {
            var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await TenantUserRepo.ListAsync(callerInfo);
        }
        /// <summary>
        /// Add a new Subtenant
        /// </summary>
        /// <returns>successful operation</returns>
        [HttpPost, Route("subtenant")]
        public virtual async Task<ActionResult<Subtenant>> AddSubtenant([FromBody] Subtenant body)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Update an existing Subtenant
        /// </summary>
        /// <returns>successful operation</returns>
        [HttpPut, Route("subtenant")]
        public virtual async Task<ActionResult<Subtenant>> UpdateSubtenant([FromBody] Subtenant body)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// List all Subtenants
        /// </summary>
        /// <returns>successful operation</returns>
        [HttpGet, Route("subtenant/listSubtenants")]
        public virtual async Task<ActionResult<System.Collections.Generic.ICollection<Subtenant>>> ListSubtenants()
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// See pet database
        /// </summary>
        /// <param name="store">Store to seed</param>
        /// <param name="numPets">Number of pets to seed</param>
        /// <returns>Success</returns>
        [HttpGet, Route("subtenant/seedPets/{store}/{numPets}")]
        public virtual async Task<IActionResult> SeedPets(string store, int numPets)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Suspend TenantUser
        /// </summary>
        /// <param name="tenantUser">tenantUser login</param>
        /// <returns>Success</returns>
        [HttpGet, Route("suspendTenantUser/{tenantUser}")]
        public virtual async Task<IActionResult> SuspendTenantUser(string tenantUser)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Find tenantUser by ID
        /// </summary>
        /// <param name="tenantUserId">ID of tenantUser that needs to be fetched</param>
        /// <returns>successful operation</returns>
        [HttpGet, Route("tenantUser/{tenantUserId}")]
        public virtual async Task<ActionResult<TenantUser>> GetTenantUserById(string tenantUserId)
        {
            var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await TenantUserRepo.ReadAsync(callerInfo, tenantUserId);
        }
        /// <summary>
        /// Delete tenantUser by ID
        /// </summary>
        /// <param name="tenantUserId">ID of tenantUser that needs to be deleted</param>
        /// <returns>Success</returns>
        [HttpDelete, Route("tenantUser/delete/{tenantUserId}")]
        public virtual async Task<IActionResult> DeleteTenantUser(string tenantUserId)
        {
            var callerInfo = await AdminModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await TenantUserRepo.DeleteAsync(callerInfo, tenantUserId);
        }
        /// <summary>
        /// Find Subtenant by ID
        /// </summary>
        /// <param name="subtenantId">ID of Subtenant that needs to be fetched</param>
        /// <returns>successful operation</returns>
        [HttpGet, Route("subtenant/{subtenantId}")]
        public virtual async Task<ActionResult<Subtenant>> GetSubtenantById(string subtenantId)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Delete Subtenant by ID
        /// </summary>
        /// <param name="subtenantId">ID of Subtenant that needs to be deleted</param>
        /// <returns>Success</returns>
        [HttpDelete, Route("subtenant/delete/{subtenantId}")]
        public virtual async Task<IActionResult> DeleteSubtenant(string subtenantId)
        {
            throw new NotImplementedException();
        }
		public IAdminModuleAuthorization AdminModuleAuthorization { get; set; }
		public ICategoryRepo CategoryRepo { get; set; }
		public ITagRepo TagRepo { get; set; }
		public IPetRepo PetRepo { get; set; }
		public IOrderRepo OrderRepo { get; set; }
		public ITenantUserRepo TenantUserRepo { get; set; }
		protected virtual void Init() { }
    }


}
