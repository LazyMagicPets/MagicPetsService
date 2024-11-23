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

namespace ConsumerModule
{
    using System = global::System;

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0))")]
    public interface IConsumerModuleController
    {

        /// <summary>
        /// Get user preferences
        /// </summary>

        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Preferences>> GetPreferences();

        /// <summary>
        /// Update user preferences
        /// </summary>


        /// <returns>successful operation</returns>

        System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Preferences>> UpdatePreferences(Preferences body);

    }

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.0.3.0 (NJsonSchema v11.0.0.0 (Newtonsoft.Json v13.0.3.0))")]

    public abstract partial class ConsumerModuleController : Microsoft.AspNetCore.Mvc.Controller,IConsumerModuleController
    {

        /// <summary>
        /// Get user preferences
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpGet, Microsoft.AspNetCore.Mvc.Route("{prefix}/preferences")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Preferences>> GetPreferences()
        {
            var callerInfo = await consumerModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await preferencesRepo.ReadAsync(callerInfo, callerInfo.LzUserId);
        }
        /// <summary>
        /// Update user preferences
        /// </summary>
        /// <returns>successful operation</returns>
        [Microsoft.AspNetCore.Mvc.HttpPost, Microsoft.AspNetCore.Mvc.Route("{prefix}/preferences")]
        public virtual async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Preferences>> UpdatePreferences([Microsoft.AspNetCore.Mvc.FromBody] Preferences body)
        {
            var callerInfo = await consumerModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await preferencesRepo.UpdateAsync(callerInfo, body);
        }
		protected IConsumerModuleAuthorization consumerModuleAuthorization;
		protected ILzNotificationRepo lzNotificationRepo;
		protected ILzNotificationsPageRepo lzNotificationsPageRepo;
		protected ILzSubscriptionRepo lzSubscriptionRepo;
		protected IPreferencesRepo preferencesRepo;
		protected ICategoryRepo categoryRepo;
		protected ITagRepo tagRepo;
		protected IPetRepo petRepo;
		protected IOrderRepo orderRepo;
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
