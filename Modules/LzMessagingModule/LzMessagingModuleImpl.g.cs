
//----------------------
// <auto-generated>
//     Generated by LazyMagic, do not edit directly. Changes will be overwritten.
//     Create your own cs file for this partial class and implement any endpoint methods 
//     not having an x-lz-gencall attribute.
//     Note that NSWAG is hardcoded to add 'Controller' to the classname.
// </auto-generated>
//----------------------
namespace LzMessagingModule
{
    public partial class LzMessagingModuleControllerImpl : LzMessagingModuleController
    {
        public LzMessagingModuleControllerImpl(
            ILzMessagingModuleAuthorization lzMessagingModuleAuthorization,
			ILzNotificationRepo lzNotificationRepo,
			ILzNotificationsPageRepo lzNotificationsPageRepo,
			ILzSubscriptionRepo lzSubscriptionRepo,
			ICategoryRepo categoryRepo,
			ITagRepo tagRepo,
			IPetRepo petRepo,
			IOrderRepo orderRepo
            ) 
        {
            this.lzMessagingModuleAuthorization = lzMessagingModuleAuthorization;
			this.lzNotificationRepo = lzNotificationRepo;
			this.lzNotificationsPageRepo = lzNotificationsPageRepo;
			this.lzSubscriptionRepo = lzSubscriptionRepo;
			this.categoryRepo = categoryRepo;
			this.tagRepo = tagRepo;
			this.petRepo = petRepo;
			this.orderRepo = orderRepo;

            Init();
        }
    }
}
