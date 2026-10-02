// Hand-written implementations of the host hooks Program.g.cs declares. Survives regeneration.
using MagicPets.Hosting;

public partial class Program
{
    // Which pools may call each module; see ModuleAuthEnforcement.
    static partial void ConfigureHostBuilder(WebApplicationBuilder builder) =>
        builder.Services.AddModuleAuthEnforcement();
}
