
namespace ConsumerModule
 
{

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public interface IConsumerModuleController
    {

        /// <summary>
        /// Get user preferences
        /// </summary>

        /// <returns>successful operation</returns>

        Task<ActionResult<Preferences>> GetPreferences();

        /// <summary>
        /// Update user preferences
        /// </summary>


        /// <returns>successful operation</returns>

        Task<ActionResult<Preferences>> UpdatePreferences(Preferences body);

    }

}
