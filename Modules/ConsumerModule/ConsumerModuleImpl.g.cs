
//----------------------
// <auto-generated>
//     Generated by LazyMagic, do not edit directly. Changes will be overwritten.
//     Create your own cs file for this partial class and implement any endpoint methods 
//     not having an x-lz-gencall attribute.
//     Note that NSWAG is hardcoded to add 'Controller' to the classname.
// </auto-generated>
//----------------------
namespace ConsumerModule
{
    public partial class ConsumerModuleControllerImpl : ConsumerModuleController
    {
        public ConsumerModuleControllerImpl(
            IConsumerModuleAuthorization consumerModuleAuthorization,
			IPreferencesRepo preferencesRepo,
			ICategoryRepo categoryRepo,
			ITagRepo tagRepo,
			IPetRepo petRepo,
			IOrderRepo orderRepo,
			ILzNotificationRepo lzNotificationRepo,
			ILzNotificationsPageRepo lzNotificationsPageRepo,
			ILzSubscriptionRepo lzSubscriptionRepo
            ) 
        {
            this.consumerModuleAuthorization = consumerModuleAuthorization;
			this.preferencesRepo = preferencesRepo;
			this.categoryRepo = categoryRepo;
			this.tagRepo = tagRepo;
			this.petRepo = petRepo;
			this.orderRepo = orderRepo;
			this.lzNotificationRepo = lzNotificationRepo;
			this.lzNotificationsPageRepo = lzNotificationsPageRepo;
			this.lzSubscriptionRepo = lzSubscriptionRepo;

            Init();
        }
    }
}
