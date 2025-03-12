
//----------------------
// <auto-generated>
//     Generated by LazyMagic. Do not modify, your changes will be overwritten.
//     If you need to register additional services, do it in a separate class.
//     Also, if you need to register a service with a different lifetime, do it in a seprate class.
//     Note that we are using Try* so if you register a service with the same interface first, that 
//     registration will be used.   
//     We very intentionally use Singletons for our Repos. This is because we want to be able to 
//     maintain state for caching etc. when necessary. 
// </auto-generated>
//----------------------
namespace SharedSchemaRepo;
public static partial class SharedSchemaRepoExtensions
{
    public static IServiceCollection AddSharedSchemaRepo(this IServiceCollection services)
    {

        services.TryAddAWSService<Amazon.DynamoDBv2.IAmazonDynamoDB>();
		services.TryAddSingleton<ICategoryRepo, CategoryRepo>();
		services.TryAddSingleton<ITagRepo, TagRepo>();
		services.TryAddSingleton<IPetRepo, PetRepo>();


        AddCustom(services);    
        return services;
    }
    // Implement this partial method in a separate file to add custom service registrations
    // Note that this method doesn't return services as partial methods don't allow return 
    // values other than void. Returning the collection is normally implemented to support 
    // method chaining, but that is not required here.
    static partial void AddCustom(IServiceCollection services);

}
