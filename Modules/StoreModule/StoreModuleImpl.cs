﻿namespace StoreModule
{
    public partial class StoreModuleControllerImpl : StoreModuleController
    {
        public override async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<Pet>> UpdatePet([Microsoft.AspNetCore.Mvc.FromBody] Pet body)
        {

            if (!ModelState.IsValid)
            {
                // Log the errors
                var modelStateErrors = new Dictionary<string, object>();
                foreach (var kvp in ModelState)
                {
                    var errors = kvp.Value.Errors
                        .Select(e => new
                        {
                            ErrorMessage = e.ErrorMessage,
                            Exception = e.Exception?.GetType().Name,
                            ExceptionMessage = e.Exception?.Message
                        })
                        .ToList();

                    modelStateErrors[kvp.Key] = new
                    {
                        RawValue = kvp.Value.RawValue,
                        AttemptedValue = kvp.Value.AttemptedValue,
                        ValidationState = kvp.Value.ValidationState.ToString(),
                        Errors = errors
                    };
                }
                return BadRequest(modelStateErrors);
            }

            var callerInfo = await storeModuleAuthorization.GetCallerInfoAsync(this.Request);
            return await petRepo.UpdateAsync(callerInfo, body);
        }

    }
}