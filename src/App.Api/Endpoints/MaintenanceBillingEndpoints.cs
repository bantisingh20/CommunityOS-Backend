using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Exceptions;
using App.Application.MaintenanceBilling;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

public static class MaintenanceBillingEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceBillingEndpoints(this IEndpointRouteBuilder app)
    {
        MapChargeEndpoints(app);
        MapBillEndpoints(app);
        MapPaymentEndpoints(app);
        return app;
    }

    private static void MapChargeEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/maintenance-charges");

        group.MapPost("/", async (SetChargeBody body, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var dto = await svc.SetChargeAsync(new SetMaintenanceChargeRequest(
                    body.CommunityId, body.UnitId, body.Amount, body.Label, body.IsActive), ct);
                return Results.Ok(ApiResult<MaintenanceChargeDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "MaintenanceCharge"));

        group.MapPost("/bulk", async (BulkSetChargeBody body, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var result = await svc.BulkSetChargesAsync(new BulkSetMaintenanceChargeRequest(
                    body.CommunityId, body.UnitIds ?? [], body.Amount, body.Label, body.IsActive, body.ApplyToAllUnits), ct);
                return Results.Ok(ApiResult<BulkSetMaintenanceChargeResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "MaintenanceCharge"));

        group.MapGet("/", async (Guid communityId, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var items = await svc.ListChargesAsync(communityId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<MaintenanceChargeDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MaintenanceCharge"));
    }

    private static void MapBillEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/maintenance-bills");

        group.MapPost("/generate", async (GenerateBillsBody body, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var result = await svc.GenerateBillsAsync(new GenerateBillsRequest(
                    body.CommunityId, body.PeriodYear, body.PeriodMonth, body.DueDateUtc), ct);
                return Results.Ok(ApiResult<GenerateBillsResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "MaintenanceBill"));

        group.MapGet("/", async (
                string? sort,
                string? status,
                Guid? unitId,
                int? periodYear,
                int? periodMonth,
                int? page,
                int? pageSize,
                IMaintenanceBillingService svc,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "unitId", unitId);
                AddEquals(filters, "periodYear", periodYear);
                AddEquals(filters, "periodMonth", periodMonth);

                var data = await svc.ListBillsAsync(BuildParameters(page, pageSize, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<MaintenanceBillDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MaintenanceBill"));

        group.MapGet("/me/due", async (IMaintenanceBillingService svc, App.Application.Config.IConfigService config, CancellationToken ct) =>
            {
                var days = await config.GetIntAsync(App.Application.Config.ConfigKeys.MaintenanceBillReminderDaysBeforeDue, 10, null, ct);
                var items = await svc.ListMyDueBillsAsync(days, ct);
                return Results.Ok(ApiResult<IReadOnlyList<MaintenanceBillDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MaintenanceBill"));

        group.MapGet("/{id:guid}", async (Guid id, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var dto = await svc.GetBillAsync(id, ct) ?? throw new NotFoundException("Maintenance bill not found.");
                return Results.Ok(ApiResult<MaintenanceBillDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MaintenanceBill"));

        group.MapGet("/{id:guid}/payments", async (Guid id, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var items = await svc.ListPaymentsForBillAsync(id, ct);
                return Results.Ok(ApiResult<IReadOnlyList<MaintenancePaymentDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "MaintenancePayment"));

        group.MapPost("/{id:guid}/waive", async (Guid id, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var dto = await svc.WaiveBillAsync(id, ct);
                return Results.Ok(ApiResult<MaintenanceBillDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "MaintenanceBill"));
    }

    private static void MapPaymentEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/maintenance-payments");

        group.MapPost("/", async (RecordPaymentBody body, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var dto = await svc.RecordPaymentAsync(new RecordMaintenancePaymentRequest(
                    body.MaintenanceBillId, body.Amount, body.Method!, body.Reference, body.PaidAtUtc,
                    SubmittedByResident: false, AutoApprove: true), ct);
                return Results.Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "MaintenancePayment"));

        group.MapPost("/submit", async (RecordPaymentBody body, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var dto = await svc.RecordPaymentAsync(new RecordMaintenancePaymentRequest(
                    body.MaintenanceBillId, body.Amount, body.Method!, body.Reference, body.PaidAtUtc,
                    SubmittedByResident: true, AutoApprove: false), ct);
                return Results.Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "MaintenancePayment"));

        group.MapGet("/pending", async (Guid communityId, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var items = await svc.ListPendingPaymentsAsync(communityId, ct);
                return Results.Ok(ApiResult<IReadOnlyList<PendingPaymentDto>>.Ok(items, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "MaintenancePayment"));

        group.MapPost("/{id:guid}/approve", async (Guid id, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var dto = await svc.ApprovePaymentAsync(id, ct);
                return Results.Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "MaintenancePayment"));

        group.MapPost("/{id:guid}/reject", async (Guid id, IMaintenanceBillingService svc, CancellationToken ct) =>
            {
                var dto = await svc.RejectPaymentAsync(id, ct);
                return Results.Ok(ApiResult<MaintenancePaymentDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "MaintenancePayment"));
    }

    private static void AddEquals(List<FilterTerm> filters, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, value));
        }
    }

    private static void AddEquals(List<FilterTerm> filters, string field, Guid? value)
    {
        if (value is { } v)
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, v.ToString()));
        }
    }

    private static void AddEquals(List<FilterTerm> filters, string field, int? value)
    {
        if (value is { } v)
        {
            filters.Add(new FilterTerm(field, FilterOperator.Equals, v.ToString()));
        }
    }

    private static ListQueryParameters BuildParameters(int? page, int? pageSize, string? sort, List<FilterTerm> filters) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

    public sealed class SetChargeBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Required] public Guid UnitId { get; init; }
        [Range(0, 99999999)] public decimal Amount { get; init; }
        [MaxLength(200)] public string? Label { get; init; }
        public bool IsActive { get; init; } = true;
    }

    public sealed class BulkSetChargeBody
    {
        [Required] public Guid CommunityId { get; init; }
        public IReadOnlyList<Guid>? UnitIds { get; init; }
        [Range(0, 99999999)] public decimal Amount { get; init; }
        [MaxLength(200)] public string? Label { get; init; }
        public bool IsActive { get; init; } = true;
        public bool ApplyToAllUnits { get; init; }
    }

    public sealed class GenerateBillsBody
    {
        [Required] public Guid CommunityId { get; init; }
        [Range(2000, 3000)] public int PeriodYear { get; init; }
        [Range(1, 12)] public int PeriodMonth { get; init; }
        public DateTime? DueDateUtc { get; init; }
    }

    public sealed class RecordPaymentBody
    {
        [Required] public Guid MaintenanceBillId { get; init; }
        [Range(0.01, 99999999)] public decimal Amount { get; init; }
        [Required(AllowEmptyStrings = false)] [MaxLength(50)] public string? Method { get; init; }
        [MaxLength(200)] public string? Reference { get; init; }
        public DateTime? PaidAtUtc { get; init; }
    }
}
