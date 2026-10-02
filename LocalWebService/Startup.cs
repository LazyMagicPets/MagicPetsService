// Hand-written implementations of the host hooks Startup.g.cs declares. Survives regeneration.
using MagicPets.Hosting;

public partial class Startup
{
    // LazyMagic takes the caller's identity only from a validated principal, so this host must
    // validate tokens like AppHost does: one scheme per LZ_AUTH_{NAME}_USERPOOLID, and the same
    // per-module pool rules. See ModuleAuthEnforcement.
    partial void ConfigureHostServices(IServiceCollection services) =>
        services.AddCognitoSchemesFromEnvironment().AddModuleAuthEnforcement();
}
