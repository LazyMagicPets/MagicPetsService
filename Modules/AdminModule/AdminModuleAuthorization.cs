namespace AdminModule
{
    /// <summary>
    /// AdminModuleAuthorization
    /// Here we implement a brain-dead authorization scheme that allows only the 
    /// user named "Administrator" to do anything in this Api. 
    /// A more realistic implementation would check the user's permissions in a
    /// database or some other external source.
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
