
//----------------------
// <auto-generated>
//     Generated by LazyMagic, do not edit directly. Changes will be overwritten.
//     Implement another class for registrations not directly generated by LazyMagic.
// </auto-generated>
//----------------------
namespace StoreModule
{
    public static partial class StoreModuleRegistrations 
    {
        public static IServiceCollection AddStoreModule(this IServiceCollection services) 
        {
            services.TryAddSingleton<IStoreModuleAuthorization, StoreModuleAuthorization>();
            services.TryAddSingleton<IStoreModuleController, StoreModuleControllerImpl>();
            services.AddSharedSchemaRepo();
			services.AddStoreSchemaRepo();
            CustomConfigurations(services);
            return services;            
        }
        static partial void CustomConfigurations(IServiceCollection sdervices);
    }
}
