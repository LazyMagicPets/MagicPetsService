// This class avoids a unnecessary .NET issue when placing controllers in separate projects.
// Specifically it allows us to Project Sdk="Microsoft.Net.Sdk.Web" so we can use the AspNetCore built into
// the runtime instead of linking in a redundant lib.
public partial class FakeEntryPoint
{
    public static void Main()
    { }
}
