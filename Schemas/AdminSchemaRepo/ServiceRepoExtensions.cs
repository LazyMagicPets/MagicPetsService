using Amazon.CloudFront;
using Amazon.CloudFrontKeyValueStore;
namespace AdminSchemaRepo;

public static partial class AdminSchemaRepoExtensions
{
    static partial void AddCustom(IServiceCollection services)
    {
        services.TryAddAWSService<IAmazonCloudFrontKeyValueStore>();
        services.TryAddAWSService<IAmazonCloudFront>();
        services.TryAddSingleton<IKvsArnResolver, KvsArnResolver>();
        services.TryAddSingleton<ISubtenantRepo, SubtenantRepo>();
    }
}
