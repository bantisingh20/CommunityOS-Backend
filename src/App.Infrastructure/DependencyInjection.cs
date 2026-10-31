using App.Application.Abstractions;
using App.Application.Abstractions.Audit;
using App.Application.Abstractions.Idempotency;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Community;
using App.Application.Config;
using App.Application.Files;
using App.Application.Gate;
using App.Application.Helpdesk;
using App.Application.Notifications;
using App.Application.Parcel;
using App.Application.Querying;
using App.Application.Identity;
using App.Application.Maintenance;
using App.Application.Residents;
using App.Application.Amenities;
using App.Application.Parking;
using App.Infrastructure.Amenities;
using App.Infrastructure.Parking;
using App.Infrastructure.Audit;
using App.Infrastructure.Auth;
using App.Infrastructure.Community;
using App.Infrastructure.Config;
using App.Infrastructure.Files;
using App.Infrastructure.Gate;
using App.Infrastructure.Helpdesk;
using App.Infrastructure.Idempotency;
using App.Infrastructure.Identity;
using App.Infrastructure.Maintenance;
using App.Infrastructure.Notifications;
using App.Infrastructure.Parcel;
using App.Infrastructure.Persistence;
using App.Infrastructure.Residents;
using App.Infrastructure.Persistence.Interceptors;
using App.Infrastructure.Querying;
using App.Infrastructure.Security;
using App.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace App.Infrastructure;


public static class DependencyInjection
{
    
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureProvider = null)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddOptions<PaginationOptions>()
            .Bind(configuration.GetSection(PaginationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAsyncQueryExecutor, EfAsyncQueryExecutor>();

        services.AddMemoryCache();
        services.AddScoped<IConfigService, ConfigService>();


        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IConfigAuditSink, AuditConfigAuditSink>();

        services.AddScoped<IPageSizePolicy, ConfigPageSizePolicy>();

        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserRoleService, UserRoleService>();
        services.AddScoped<IPermissionCatalog, PermissionCatalog>();
        services.AddScoped<IEffectivePermissionQuery, EffectivePermissionQuery>();
        services.AddScoped<App.Application.Identity.IMenuService, Identity.MenuService>();
        services.AddScoped<App.Application.Identity.IAdminUserService, Identity.AdminUserService>();

        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ICommunityService, CommunityService>();
        services.AddScoped<IHierarchyService, HierarchyService>();

        services.AddScoped<IUnitService, UnitService>();
        services.AddOptions<BulkOptions>()
            .Bind(configuration.GetSection(BulkOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IUnitBulkService, UnitBulkService>();

        services.AddScoped<IResidentService, ResidentService>();


        services.AddScoped<IUnitHistoryService, UnitHistoryService>();

        services.AddScoped<IResidentAssetsService, ResidentAssetsService>();


        services.AddScoped<ICommunicationPreferenceService, CommunicationPreferenceService>();
        services.AddScoped<ICommunicationPreferences, ResidentCommunicationPreferences>();


        services.AddScoped<IGateService, GateService>();

        services.AddScoped<ISecurityOpsService, SecurityOpsService>();

        services.AddScoped<IParcelService, ParcelService>();

        services.AddScoped<IHelpdeskService, HelpdeskService>();

        services.AddScoped<IMaintenanceService, MaintenanceService>();


        services.AddScoped<IAmenityService, AmenityService>();


        services.AddScoped<IParkingService, ParkingService>();

        services.AddScoped<App.Application.MaintenanceBilling.IMaintenanceBillingService, App.Infrastructure.MaintenanceBilling.MaintenanceBillingService>();

        services.AddScoped<App.Application.MyTasks.IMyTasksService, App.Infrastructure.MyTasks.MyTasksService>();

        services.AddScoped<IAnnouncementService, AnnouncementService>();

        services.TryAddScoped<IAccessControlIntegration, LoggingAccessControlIntegration>();

        services.AddScoped<AuthorizationService>();
        services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<AuthorizationService>());
        services.AddScoped<IAuthorizationGuard>(sp => sp.GetRequiredService<AuthorizationService>());

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<JwtIssuerOptions>()
            .Bind(configuration.GetSection(JwtIssuerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFileBlobStore, LocalFileBlobStore>();
        services.AddScoped<IFileService, FileService>();

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<INotificationTemplateRenderer, PlaceholderTemplateRenderer>();
        services.AddScoped<INotificationRecipientResolver, DeviceRecipientResolver>();
       
        services.TryAddScoped<ICommunicationPreferences, DefaultCommunicationPreferences>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<App.Application.Notifications.INotificationTemplateAdminService, Notifications.NotificationTemplateAdminService>();
        services.AddScoped<App.Application.Notifications.INotificationInboxService, Notifications.NotificationInboxService>();

        services.AddOptions<NotificationWorkerOptions>()
            .Bind(configuration.GetSection(NotificationWorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IOneSignalClient, OneSignalHttpClient>((sp, client) =>
        {
            var notif = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            client.BaseAddress = new Uri(notif.OneSignalApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(notif.HttpTimeoutSeconds);
        });
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        services.AddOptions<App.Application.Messaging.SmtpOptions>()
            .Bind(configuration.GetSection(App.Application.Messaging.SmtpOptions.SectionName));
        services.AddOptions<App.Application.Messaging.MessageOutboxWorkerOptions>()
            .Bind(configuration.GetSection(App.Application.Messaging.MessageOutboxWorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<App.Application.Messaging.IMessageSender, Messaging.MessageSender>();
        services.AddScoped<App.Application.Messaging.IMessageOutboxQueryService, Messaging.MessageOutboxQueryService>();
        services.AddScoped<App.Application.Notifications.INotificationOutboxQueryService, Notifications.NotificationOutboxQueryService>();
        services.AddScoped<App.Application.Messaging.IEmailChannelSender, Messaging.SmtpEmailChannelSender>();
        services.TryAddScoped<App.Application.Messaging.ISmsChannelSender, Messaging.NotConfiguredSmsChannelSender>();
        services.TryAddScoped<App.Application.Messaging.IWhatsAppChannelSender, Messaging.NotConfiguredWhatsAppChannelSender>();


        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IAuthTokenIssuer, JwtAuthTokenIssuer>();
        services.AddScoped<ISessionRevocationStore, SessionRevocationStore>();
        services.TryAddScoped<IOtpDeliverer, LoggingOtpDeliverer>(); // Notification_Service (Task 7.1) replaces later.
        services.AddScoped<IAuthService, AuthService>();

        // Public self-service onboarding (create your own community + admin). Scoped: it uses the
        // request-scoped IAuthService to log the new admin in after provisioning.
        services.AddScoped<App.Application.Onboarding.IOnboardingService, App.Infrastructure.Onboarding.OnboardingService>();

        // Resident self-onboarding + admin approval (mobile login/registration flow).
        services.AddScoped<App.Application.Onboarding.IResidentOnboardingService, App.Infrastructure.Onboarding.ResidentOnboardingService>();

        // Fail-closed default; the request pipeline (Task 2.2) replaces this with a principal-derived
        // context. TryAdd so a host registering the real one first always wins.
        services.TryAddScoped<ITenantContext, NullTenantContext>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            if (configureProvider is not null)
            {
                configureProvider(options);
            }
            else
            {
                var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                options.UseSqlServer(dbOptions.ConnectionString, sql =>
                    sql.CommandTimeout(dbOptions.CommandTimeoutSeconds));
            }

            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        return services;
    }
}
