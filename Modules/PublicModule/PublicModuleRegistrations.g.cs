
//----------------------
// <auto-generated>
//     Generated by LazyMagic, do not edit directly. Changes will be overwritten.
//     Implement another class for registrations not directly generated by LazyMagic.
// </auto-generated>
//----------------------
namespace PublicModule
{
    public static partial class PublicModuleRegistrations 
    {
        public static IServiceCollection AddPublicModule(this IServiceCollection services) 
        {
            services.TryAddSingleton<IPublicModuleAuthorization, PublicModuleAuthorization>();
            services.TryAddSingleton<IPublicModuleController, PublicModuleController>();
            services.AddSharedSchemaRepo();
			services.AddStoreSchemaRepo();
            CustomConfigurations(services);
            return services;            
        }
        static partial void CustomConfigurations(IServiceCollection sdervices);
    }
}
