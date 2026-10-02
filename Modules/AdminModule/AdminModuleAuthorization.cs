namespace AdminModule
{
    /// <summary>
    /// AdminModuleAuthorization
    /// Every caller that reaches this module is Admin. That is safe only because the host lets
    /// nothing but a validated tenantauth (employee) token reach it - see ModuleAuthEnforcement in
    /// Containers/AppHost. Until a finer rule exists (e.g. a Cognito group), every store employee is
    /// an admin.
    /// </summary>
    public partial class AdminModuleAuthorization
    {
        public override async Task<bool> HasPermissionAsync(string methodName, List<string> userPermissions)
        {
            await Task.Delay(0);
            if(userPermissions.Contains("Admin"))
            {
                return true;
            }

            return false;
        }

        protected override async Task<List<string>> GetUserPermissionsAsync(string lzUserId, string userName, string tenancy)
        {
            return new List<string> { "Admin" };
            await Task.Delay(0);
        }
    }
}
