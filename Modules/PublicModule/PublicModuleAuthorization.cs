using LazyMagic.Shared;
using System.Runtime.CompilerServices;

namespace PublicModule
{
    /// <summary>
    /// This class is used to define the permissions for the PublicModule module.
    /// Since this module is public, it does not require authentication, we implement
    /// - authenticate = false
    /// - override GetUserPermissionsAsync to return an empty list of permissions.
    /// - override HasPermissionAsync to always return true.
    /// </summary>
    public partial class PublicModuleAuthorization
    {
        public PublicModuleAuthorization ()
        {
            authenticate = false; // This module does not require authentication
        }
        protected override async Task<List<string>> GetUserPermissionsAsync(string lzUserId, string userName, string table)
        {
            Console.WriteLine("PublicModuleAuthorization.GetUserPermissionsAsync called");
            await Task.Delay(0);
            // Since default methods can't access instance state, we call the helper method that can.
            return new List<string>();
        }
        // Public API has no permission checks
        public override async Task<bool> HasPermissionAsync(string methodName, List<string> userPermissions)
        {
            await Task.Delay(0);
            return true;
        }
    }
}
