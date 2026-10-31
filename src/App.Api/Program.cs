using App.Api.Endpoints;
using App.Api.Middleware;
using App.Api.Observability;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Contracts.Serialization;
using App.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddSingleton(EnvelopeJson.CreateOptions());

builder.Services.AddInfrastructure(builder.Configuration);

if (builder.Configuration.GetValue<bool>("Notification:RunOutboxWorkerInApi"))
{
    builder.Services.AddHostedService<App.Api.Notifications.ApiOutboxDrainService>();
}

if (builder.Configuration.GetValue<bool>("MessageOutboxWorker:RunInApi"))
{
    builder.Services.AddHostedService<App.Infrastructure.Messaging.MessageOutboxDrainService>();
}

if (builder.Configuration.GetValue<bool?>("WalkInExpiryWorker:RunInApi") ?? true)
{
    builder.Services.AddHostedService<App.Api.Gate.ApiWalkInExpiryService>();
}

builder.Services.AddRequestPipelineSecurity(builder.Configuration);

builder.AddObservability();

builder.Services.AddApiRateLimiting(builder.Configuration);

const string CorsPolicy = "AdminPortal";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                .WithExposedHeaders("X-Correlation-Id");
        }
    });
});

var app = builder.Build();

app.UseExceptionEnvelope();

app.UseCorrelationId();

app.UseCors(CorsPolicy);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();

app.UseAuthorization();
app.UsePermissionAuthorization();

app.UseRateLimiter();

app.MapObservabilityEndpoints();

app.MapPipelineDemoEndpoints();

app.MapConfigEndpoints();
app.MapNotificationTemplateEndpoints();

// Audit read endpoint (Task 4.1): tenant-scoped, paginated audit-log query (View-gated).
app.MapAuditEndpoints();
app.MapMessageOutboxEndpoints();
app.MapNotificationOutboxAdminEndpoints();
app.MapMenuEndpoints();
app.MapAdminUserEndpoints();

app.MapFileEndpoints();

app.MapAuthEndpoints();
app.MapOnboardingEndpoints();
app.MapResidentOnboardingEndpoints();
// RBAC management: every endpoint carries [RequirePermission] on its (Action, ResourceType) (Req 4.1).
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapPermissionEndpoints();
app.MapUserRoleEndpoints();

app.MapOrganizationEndpoints();
app.MapCommunityEndpoints();
app.MapHierarchyEndpoints();
app.MapUnitEndpoints();

app.MapResidentEndpoints();
app.MapUnitHistoryEndpoints();
app.MapResidentAssetEndpoints();

app.MapGateEndpoints();
app.MapSecurityOpsEndpoints();

app.MapParcelEndpoints();

app.MapHelpdeskEndpoints();
app.MapMaintenanceEndpoints();

app.MapAmenityEndpoints();
app.MapParkingEndpoints();
app.MapMaintenanceBillingEndpoints();
app.MapMyTasksEndpoints();


app.MapAnnouncementEndpoints();
app.MapNotificationEndpoints();

if (app.Environment.IsDevelopment())
{
    var devSeed = new App.Api.DevSeed.DevSeedOptions();
    app.Configuration.GetSection(App.Api.DevSeed.DevSeedOptions.SectionName).Bind(devSeed);
    if (devSeed.Enabled)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        await App.Api.DevSeed.DevDataSeeder.SeedAsync(app.Configuration, devSeed, loggerFactory);
    }
}

app.Run();

public partial class Program;

