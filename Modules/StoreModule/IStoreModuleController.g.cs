
namespace StoreModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0))")]
    public interface IStoreModuleController
    {

        /// <summary>
        /// List all pets
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Pet>>> StoreModuleListPets();

        /// <summary>
        /// Finds Pets by status
        /// </summary>

        /// <remarks>
        /// Multiple status values can be provided with comma separated strings
        /// </remarks>

        /// <param name="petStatus">Status values that need to be considered for filter</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Pet>>> StoreModuleFindPetsByStatus(System.Collections.Generic.IEnumerable<PetStatus> petStatus);

        /// <summary>
        /// Add a new pet to the store
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Pet>> StoreModuleAddPet(Pet body);

        /// <summary>
        /// Update an existing pet
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Pet>> StoreModuleUpdatePet(Pet body);

        /// <summary>
        /// Finds Pets by tags
        /// </summary>

        /// <remarks>
        /// Muliple tags can be provided with comma separated strings. Use\ \ tag1, tag2, tag3 for testing.
        /// </remarks>

        /// <param name="tags">Tags to filter by</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Pet>>> StoreModuleFindPetsByTags(System.Collections.Generic.IEnumerable<string> tags);

        /// <summary>
        /// Get all Pet Categories
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Category>>> StoreModuleGetPetCategories();

        /// <summary>
        /// Get all Pet Tags
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Tag>>> StoreModuleGetPetTags();

        /// <summary>
        /// Returns pet inventories by status
        /// </summary>

        /// <remarks>
        /// Returns a map of status codes to quantities
        /// </remarks>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.IDictionary<string, int>>> StoreModuleGetInventory();

        /// <summary>
        /// Place an order for a pet
        /// </summary>

        /// <param name="body">order placed for purchasing the pet</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Order>> StoreModulePlaceOrder(Order body);

        /// <summary>
        /// Find pet by ID
        /// </summary>

        /// <remarks>
        /// Returns a single pet
        /// </remarks>

        /// <param name="petId">ID of pet to return</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Pet>> StoreModuleGetPetById(string petId);

        /// <summary>
        /// Deletes a pet
        /// </summary>

        /// <param name="petId">Pet id to delete</param>

        /// <returns>Success</returns>

        Task<IActionResult> StoreModuleDeletePet(string petId);

        /// <summary>
        /// Find purchase order by ID
        /// </summary>

        /// <remarks>
        /// For valid response try integer IDs with value &gt;= 1 and &lt;= 10.\ \ Other values will generated exceptions
        /// </remarks>

        /// <param name="orderId">ID of pet that needs to be fetched</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Order>> StoreModuleGetOrderById(string orderId);

        /// <summary>
        /// Delete purchase order by ID
        /// </summary>

        /// <param name="orderId">ID of the order that needs to be deleted</param>

        /// <returns>Success</returns>

        Task<IActionResult> StoreModuleDeleteOrder(string orderId);

    }

}
