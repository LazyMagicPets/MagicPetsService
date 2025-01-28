//----------------------
// <auto-generated>
//     Generated using the NSwag toolchain v14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0)) (http://NSwag.org)
// </auto-generated>
//----------------------

#pragma warning disable 108 // Disable "CS0108 '{derivedDto}.ToJson()' hides inherited member '{dtoBase}.ToJson()'. Use the new keyword if hiding was intended."
#pragma warning disable 114 // Disable "CS0114 '{derivedDto}.RaisePropertyChanged(String)' hides inherited member 'dtoBase.RaisePropertyChanged(String)'. To make the current member override that implementation, add the override keyword. Otherwise add the new keyword."
#pragma warning disable 472 // Disable "CS0472 The result of the expression is always 'false' since a value of type 'Int32' is never equal to 'null' of type 'Int32?'
#pragma warning disable 612 // Disable "CS0612 '...' is obsolete"
#pragma warning disable 1573 // Disable "CS1573 Parameter '...' has no matching param tag in the XML comment for ...
#pragma warning disable 1591 // Disable "CS1591 Missing XML comment for publicly visible type or member ..."
#pragma warning disable 8073 // Disable "CS8073 The result of the expression is always 'false' since a value of type 'T' is never equal to 'null' of type 'T?'"
#pragma warning disable 3016 // Disable "CS3016 Arrays as attribute arguments is not CLS-compliant"
#pragma warning disable 8603 // Disable "CS8603 Possible null reference return"
#pragma warning disable 8604 // Disable "CS8604 Possible null reference argument for parameter"
#pragma warning disable 8625 // Disable "CS8625 Cannot convert null literal to non-nullable reference type"
#pragma warning disable 1998 // Disable "CS1998 Disable async warning."

namespace StoreModule
{
    using System = global::System;

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0))")]
    public interface IStoreModuleController
    {

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<string>> CallerInfo();

        /// <summary>
        /// Test
        /// </summary>

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<string>> TestEmployee();

        /// <summary>
        /// Add a new pet to the store
        /// </summary>


        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Pet>> AddPet(Pet body);

        /// <summary>
        /// Update an existing pet
        /// </summary>


        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Pet>> UpdatePet(Pet body);

        /// <summary>
        /// Returns pet inventories by status
        /// </summary>

        /// <remarks>
        /// Returns a map of status codes to quantities
        /// </remarks>

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IDictionary<string, int>>> GetInventory();

        /// <summary>
        /// Place an order for a pet
        /// </summary>

        /// <param name="body">order placed for purchasing the pet</param>

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Order>> PlaceOrder(Order body);

        /// <summary>
        /// Add a new user to the store
        /// </summary>


        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<User>> AddUser(User body);

        /// <summary>
        /// Update an existing user
        /// </summary>


        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<User>> UpdateUser(User body);

        /// <summary>
        /// List all users
        /// </summary>

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.ICollection<User>>> ListUsers();

        /// <summary>
        /// Deletes a pet
        /// </summary>

        /// <param name="petId">Pet id to delete</param>

        /// <returns>Success</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> DeletePet(string petId);

        /// <summary>
        /// Find purchase order by ID
        /// </summary>

        /// <remarks>
        /// For valid response try integer IDs with value &gt;= 1 and &lt;= 10.\ \ Other values will generated exceptions
        /// </remarks>

        /// <param name="orderId">ID of pet that needs to be fetched</param>

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Order>> GetOrderById(string orderId);

        /// <summary>
        /// Delete purchase order by ID
        /// </summary>

        /// <param name="orderId">ID of the order that needs to be deleted</param>

        /// <returns>Success</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> DeleteOrder(string orderId);

        /// <summary>
        /// Suspend EmployeeLambda user
        /// </summary>

        /// <param name="user">user login</param>

        /// <returns>Success</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> SuspendUser(string user);

        /// <summary>
        /// Find user by ID
        /// </summary>

        /// <param name="userId">ID of user that needs to be fetched</param>

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<User>> GetUserById(string userId);

        /// <summary>
        /// See pet database
        /// </summary>

        /// <param name="store">Store to seed</param>

        /// <param name="numPets">Number of pets to seed</param>

        /// <returns>Success</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> SeedPets(string store, int numPets);

    }

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0))")]

    public abstract partial class StoreModuleController : Microsoft.AspNetCore.Mvc.Controller,IStoreModuleController
    {

        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("callerInfo")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<string>> CallerInfo()
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Test
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("test")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<string>> TestEmployee()
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await Task.FromResult("Hello World");
        }
        /// <summary>
        /// Add a new pet to the store
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpPost, Microsoft.AspNetCore.Mvc.Route("pet")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Pet>> AddPet([Microsoft.AspNetCore.Mvc.FromBody] Pet body)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await petRepo.CreateAsync(callerInfo, body);
        }
        /// <summary>
        /// Update an existing pet
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpPut, Microsoft.AspNetCore.Mvc.Route("pet")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Pet>> UpdatePet([Microsoft.AspNetCore.Mvc.FromBody] Pet body)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await petRepo.UpdateAsync(callerInfo, body);
        }
        /// <summary>
        /// Returns pet inventories by status
        /// </summary>
        /// <remarks>
        /// Returns a map of status codes to quantities
        /// </remarks>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("order/inventory")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.IDictionary<string, int>>> GetInventory()
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Place an order for a pet
        /// </summary>
        /// <param name="body">order placed for purchasing the pet</param>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpPost, Microsoft.AspNetCore.Mvc.Route("order")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Order>> PlaceOrder([Microsoft.AspNetCore.Mvc.FromBody] Order body)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await orderRepo.CreateAsync(callerInfo, body);
        }
        /// <summary>
        /// Add a new user to the store
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpPost, Microsoft.AspNetCore.Mvc.Route("user")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<User>> AddUser([Microsoft.AspNetCore.Mvc.FromBody] User body)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await userRepo.CreateAsync(callerInfo, body);
        }
        /// <summary>
        /// Update an existing user
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpPut, Microsoft.AspNetCore.Mvc.Route("user")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<User>> UpdateUser([Microsoft.AspNetCore.Mvc.FromBody] User body)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await userRepo.UpdateAsync(callerInfo, body);
        }
        /// <summary>
        /// List all users
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("user/listUsers")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<System.Collections.Generic.ICollection<User>>> ListUsers()
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await userRepo.ListAsync(callerInfo);
        }
        /// <summary>
        /// Deletes a pet
        /// </summary>
        /// <param name="petId">Pet id to delete</param>
        /// <returns>Success</returns>
        [Microsoft.AspNetCore.Mvc.HttpDelete, Microsoft.AspNetCore.Mvc.Route("pet/{petId}")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> DeletePet(string petId)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await petRepo.DeleteAsync(callerInfo, petId);
        }
        /// <summary>
        /// Find purchase order by ID
        /// </summary>
        /// <remarks>
        /// For valid response try integer IDs with value &gt;= 1 and &lt;= 10.\ \ Other values will generated exceptions
        /// </remarks>
        /// <param name="orderId">ID of pet that needs to be fetched</param>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("order/{orderId}")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Order>> GetOrderById(string orderId)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await orderRepo.ReadAsync(callerInfo, orderId);
        }
        /// <summary>
        /// Delete purchase order by ID
        /// </summary>
        /// <param name="orderId">ID of the order that needs to be deleted</param>
        /// <returns>Success</returns>
        [Microsoft.AspNetCore.Mvc.HttpDelete, Microsoft.AspNetCore.Mvc.Route("order/{orderId}")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> DeleteOrder(string orderId)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await orderRepo.DeleteAsync(callerInfo, orderId);
        }
        /// <summary>
        /// Suspend EmployeeLambda user
        /// </summary>
        /// <param name="user">user login</param>
        /// <returns>Success</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("suspendUser/{user}")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> SuspendUser(string user)
        {
            throw new NotImplementedException();
        }
        /// <summary>
        /// Find user by ID
        /// </summary>
        /// <param name="userId">ID of user that needs to be fetched</param>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("user/{userId}")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<User>> GetUserById(string userId)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await userRepo.ReadAsync(callerInfo, userId);
        }
        /// <summary>
        /// See pet database
        /// </summary>
        /// <param name="store">Store to seed</param>
        /// <param name="numPets">Number of pets to seed</param>
        /// <returns>Success</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("pet/seedPets/{store}/{numPets}")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> SeedPets(string store, int numPets)
        {
            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            store = store.Replace("-", "_"); // replace hyphens with underscores to satisfy DynamoDB table name requirements
            return await petRepo.SeedAsync(callerInfo, store, numPets);
        }
		protected IStoreModuleAuthorization storeModuleAuthorization;
		protected ILzNotificationRepo lzNotificationRepo;
		protected ILzNotificationsPageRepo lzNotificationsPageRepo;
		protected ILzSubscriptionRepo lzSubscriptionRepo;
		protected ICategoryRepo categoryRepo;
		protected ITagRepo tagRepo;
		protected IPetRepo petRepo;
		protected IOrderRepo orderRepo;
		protected IUserRepo userRepo;
		protected virtual void Init() { }
    }


}

#pragma warning restore  108
#pragma warning restore  114
#pragma warning restore  472
#pragma warning restore  612
#pragma warning restore 1573
#pragma warning restore 1591
#pragma warning restore 8073
#pragma warning restore 3016
#pragma warning restore 8603
#pragma warning restore 8604
#pragma warning restore 8625
#pragma warning restore 1998
