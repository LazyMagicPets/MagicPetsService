
// Generated by LazyMagic - modifications will be overwritten
namespace LambdaFunc;
public partial class Startup
{
    public void ConfigureSvcs(IServiceCollection services)
    {
        services.AddLzMessagingModule();
		services.AddConsumerModule();
    }
}
