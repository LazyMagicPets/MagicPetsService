
namespace PublicModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0))")]
    public interface IPublicModuleController
    {



        /// <returns>successful operation</returns>

        Task<ActionResult<Fingerprint>> PublicModuleFingerprintCreate(Fingerprint body);

        /// <summary>
        /// List all pets
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Pet>>> PublicModuleListPets();

        /// <summary>
        /// Finds Pets by status
        /// </summary>

        /// <remarks>
        /// Multiple status values can be provided with comma separated strings
        /// </remarks>

        /// <param name="petStatus">Status values that need to be considered for filter</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Pet>>> PublicModuleFindPetsByStatus(System.Collections.Generic.IEnumerable<PetStatus> petStatus);

        /// <summary>
        /// Finds Pets by tags
        /// </summary>

        /// <remarks>
        /// Muliple tags can be provided with comma separated strings. Use\ \ tag1, tag2, tag3 for testing.
        /// </remarks>

        /// <param name="tags">Tags to filter by</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Pet>>> PublicModuleFindPetsByTags(System.Collections.Generic.IEnumerable<string> tags);

        /// <summary>
        /// Get all Pet Categories
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Category>>> PublicModuleGetPetCategories();

        /// <summary>
        /// Get all Pet Tags
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<System.Collections.Generic.ICollection<Tag>>> PublicModuleGetPetTags();

        /// <summary>
        /// Find pet by ID
        /// </summary>

        /// <remarks>
        /// Returns a single pet
        /// </remarks>

        /// <param name="petId">ID of pet to return</param>

        /// <returns>successful operation</returns>

        Task<ActionResult<Pet>> PublicModuleGetPetById(string petId);

    }

}
