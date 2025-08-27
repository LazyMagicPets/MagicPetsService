
namespace StoreModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0))")]
    public interface IStoreModuleController
    {

        /// <summary>
        /// List all pets
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Pet>>> StoreModuleListPetsAsync();

        /// <summary>
        /// Add a new pet to the store
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Pet>> StoreModuleAddPetAsync(Pet body = null);

        /// <summary>
        /// Update an existing pet
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Pet>> StoreModuleUpdatePetAsync(Pet body = null);

        /// <summary>
        /// Returns pet inventories by status
        /// </summary>

        /// <remarks>
        /// Returns a map of status codes to quantities
        /// </remarks>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.IDictionary<string, int>>> StoreModuleGetInventoryAsync();

        /// <summary>
        /// Place an order for a pet
        /// </summary>

        /// <param name="body">order placed for purchasing the pet</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Order>> StoreModulePlaceOrderAsync(Order body);

        /// <summary>
        /// Deletes a pet
        /// </summary>

        /// <param name="petId">Pet id to delete</param>

        /// <returns>Success</returns>

        Task<IActionResult> StoreModuleDeletePetAsync(string petId);

        /// <summary>
        /// Find purchase order by ID
        /// </summary>

        /// <remarks>
        /// For valid response try integer IDs with value &gt;= 1 and &lt;= 10.\ \ Other values will generated exceptions
        /// </remarks>

        /// <param name="orderId">ID of pet that needs to be fetched</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Order>> StoreModuleGetOrderByIdAsync(string orderId);

        /// <summary>
        /// Delete purchase order by ID
        /// </summary>

        /// <param name="orderId">ID of the order that needs to be deleted</param>

        /// <returns>Success</returns>

        Task<IActionResult> StoreModuleDeleteOrderAsync(string orderId);

    }

}
