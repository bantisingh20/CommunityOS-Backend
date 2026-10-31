using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Exceptions;
using App.Application.Gate;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;


public static class GateEndpoints
{
    public static IEndpointRouteBuilder MapGateEndpoints(this IEndpointRouteBuilder app)
    {
        MapVisitPassEndpoints(app);
        MapPublicPassEndpoints(app);
        MapVisitorEndpoints(app);
        MapEntryExitEndpoints(app);
        MapRecurringVisitorEndpoints(app);
        return app;
    }

    // --- Visit passes (/api/v1/visit-passes) — Req 23.x ------------------------------------------
    private static void MapVisitPassEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/visit-passes");

        // POST — create a pre-approved pass for the resident's own unit (Req 23.1). Returns the pass
        // + the one-time plaintext OTP (never returned again).
        group.MapPost("/", async (
                CreateVisitPassBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var result = await gate.CreateVisitPassAsync(new CreateVisitPassRequest(
                    body.HostUnitId,
                    body.HostResidentId,
                    body.VisitorName!,
                    body.ValidFromUtc,
                    body.ValidUntilUtc,
                    body.MaxUses,
                    body.Phone,
                    body.Email,
                    body.VehicleNumber,
                    body.VehicleType,
                    body.NumberOfPersons,
                    body.WhomToMeet,
                    body.VisitDateUtc,
                    body.PhotoFileId), ct);
                return Results.Ok(ApiResult<CreateVisitPassResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "VisitPass"));

        // POST /verify — guard verifies a pass by QR/OTP (Req 23.3, 23.4). Returns accept/reject.
        group.MapPost("/verify", async (
                VerifyVisitPassBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var result = await gate.VerifyVisitPassAsync(new VerifyVisitPassRequest(body.QrToken, body.Otp), ct);
                return Results.Ok(ApiResult<VerifyVisitPassResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            // Guard-facing gate verification grant (Req 23.3): Create on Visitor.
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Visitor"));

        group.MapPost("/scan", async (
                ScanAccessBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var result = await gate.ScanAccessAsync(
                    new ScanAccessRequest(body.QrToken, body.Otp, body.GateId, body.GateLabel), ct);
                return Results.Ok(ApiResult<ScanAccessResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "EntryExitEvent"));

        app.MapGet("/api/v1/gate/vehicle-lookup", async (
                string? plate,
                IGateService gate,
                CancellationToken ct) =>
            {
                var result = await gate.LookupVehicleAsync(plate ?? string.Empty, ct);
                return Results.Ok(ApiResult<VehicleLookupResult>.Ok(result, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Vehicle"));

        // GET — paginated/filter/sort list (Req 23.5).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                Guid? hostUnitId,
                Guid? organizationId,
                int? page,
                int? pageSize,
                IGateService gate,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "hostUnitId", hostUnitId);

                var data = await gate.ListVisitPassesAsync(BuildParameters(page, pageSize, search, sort, filters), organizationId, ct);
                return Results.Ok(ApiResult<PagedData<VisitPassDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "VisitPass"));

        // GET /{id} — fetch one, tenant/self-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.GetVisitPassAsync(id, ct)
                    ?? throw new NotFoundException("Visit pass not found.");
                return Results.Ok(ApiResult<VisitPassDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "VisitPass"));

        // POST /{id}/share — enqueue the public pass link over SMS / WhatsApp / email (Req 23.x).
        group.MapPost("/{id:guid}/share", async (
                Guid id,
                SharePassBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var result = await gate.SharePassAsync(id, new SharePassRequest(
                    body.Sms, body.WhatsApp, body.Email, body.ToPhone, body.ToEmail), ct);
                return Results.Ok(ApiResult<SharePassResult>.Ok(result, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "VisitPass"));
    }

    // --- Public pass (/api/v1/public/visit-passes) — anonymous, token-authorized -----------------
    private static void MapPublicPassEndpoints(IEndpointRouteBuilder app)
    {
        // Anonymous (no RequirePermission metadata). The opaque PublicToken is the authorization;
        // the DTO excludes PII/OTP and inlines the photo as a data URL.
        app.MapGet("/api/v1/public/visit-passes/{token}", async (
                string token,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.GetPublicPassAsync(token, ct)
                    ?? throw new NotFoundException("This pass link is invalid or has expired.");
                return Results.Ok(ApiResult<PublicVisitPassDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.AuthPolicy);
    }

    // --- Walk-ins / visitors (/api/v1/visitors) — Req 24.x ---------------------------------------
    private static void MapVisitorEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/visitors");

        // POST — guard registers a walk-in (Req 24.1). Starts pending; host unit residents notified.
        group.MapPost("/", async (
                RegisterWalkInBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var result = await gate.RegisterWalkInAsync(new RegisterWalkInRequest(
                    body.HostUnitId,
                    body.Name!,
                    body.Phone,
                    body.Category!,
                    body.PhotoFileId,
                    body.VehicleNumber,
                    body.GateLocation,
                    Email: body.Email,
                    body.IdProofType,
                    body.IdProofNumber,
                    body.Purpose,
                    body.WhomToMeet,
                    body.ExpectedDurationMinutes), ct);
                return Results.Ok(ApiResult<RegisterWalkInResult>.Ok(result, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Visitor"));

        // POST /{id}/approve — resident approves a pending walk-in for their own unit (Req 24.3).
        group.MapPost("/{id:guid}/approve", async (
                Guid id,
                ApproveWalkInBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.ApproveWalkInAsync(id, body.ApprovingResidentId, ct);
                return Results.Ok(ApiResult<VisitorDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "Visitor"));

        // POST /{id}/reject — resident rejects a pending walk-in with an optional reason (Req 24.4).
        group.MapPost("/{id:guid}/reject", async (
                Guid id,
                RejectWalkInBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.RejectWalkInAsync(id, body.RejectingResidentId, new RejectWalkInRequest(body.Reason), ct);
                return Results.Ok(ApiResult<VisitorDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Approve, "Visitor"));

        // GET — the pending-approval queue: paginated/filter(by status/category)/sort (Req 24.2).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                string? category,
                Guid? hostUnitId,
                Guid? organizationId,
                DateTime? fromDate,
                DateTime? toDate,
                int? page,
                int? pageSize,
                IGateService gate,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "category", category);
                AddEquals(filters, "hostUnitId", hostUnitId);
                // Registered-date window (createdAtUtc). The client sends start-of-day/end-of-day for
                // a "today" default; >= from and <= to scope the walk-in list to that range.
                if (fromDate is { } f)
                {
                    filters.Add(new FilterTerm("createdAtUtc", FilterOperator.GreaterThanOrEqual, f.ToUniversalTime().ToString("o")));
                }
                if (toDate is { } t)
                {
                    filters.Add(new FilterTerm("createdAtUtc", FilterOperator.LessThanOrEqual, t.ToUniversalTime().ToString("o")));
                }

                var data = await gate.ListVisitorsAsync(BuildParameters(page, pageSize, search, sort, filters), organizationId, ct);
                return Results.Ok(ApiResult<PagedData<VisitorDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Visitor"));

        // GET /{id} — fetch one, tenant/self-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.GetVisitorAsync(id, ct)
                    ?? throw new NotFoundException("Visitor not found.");
                return Results.Ok(ApiResult<VisitorDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Visitor"));
    }

    // --- Entry/exit (/api/v1/entry-events) — Req 25.3–25.5 ---------------------------------------
    private static void MapEntryExitEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/entry-events");

        // POST /entry — record an entry against exactly one admitting authorization (Req 25.3).
        group.MapPost("/entry", async (
                RecordEntryBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.RecordEntryAsync(new RecordEntryRequest(
                    body.VisitorId,
                    body.VisitPassId,
                    body.RecurringVisitorId,
                    body.GateId,
                    body.GateLabel,
                    body.VehicleNumber), ct);
                return Results.Ok(ApiResult<EntryExitEventDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "EntryExitEvent"));

        // POST /exit — close the open entry for the same authorization (Req 25.3).
        group.MapPost("/exit", async (
                RecordExitBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.RecordExitAsync(new RecordExitRequest(
                    body.VisitorId,
                    body.VisitPassId,
                    body.RecurringVisitorId,
                    body.GateId,
                    body.GateLabel), ct);
                return Results.Ok(ApiResult<EntryExitEventDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "EntryExitEvent"));

        // GET — the entry/exit log: paginated/filter(by category/open/date)/sort (Req 25.4, 25.5).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? category,
                bool? open,
                Guid? destinationUnitId,
                Guid? visitorId,
                int? page,
                int? pageSize,
                IGateService gate,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "category", category);
                AddEquals(filters, "destinationUnitId", destinationUnitId);
                AddEquals(filters, "visitorId", visitorId);
                if (open is { } o)
                {
                    // "open" true = still on-site (exit null); allow-listed as a bool predicate.
                    filters.Add(new FilterTerm("open", FilterOperator.Equals, o ? "true" : "false"));
                }

                var data = await gate.ListEntryExitEventsAsync(BuildParameters(page, pageSize, search, sort, filters), ct);
                return Results.Ok(ApiResult<PagedData<EntryExitEventDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "EntryExitEvent"));
    }

    // --- Recurring visitors (/api/v1/recurring-visitors) — Req 25.2 ------------------------------
    private static void MapRecurringVisitorEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/recurring-visitors");

        // POST — resident defines a recurring visitor for their own unit (Req 25.2).
        group.MapPost("/", async (
                CreateRecurringVisitorBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.CreateRecurringVisitorAsync(new CreateRecurringVisitorRequest(
                    body.HostUnitId,
                    body.HostResidentId,
                    body.Name!,
                    body.Phone,
                    body.Category!,
                    body.DaysOfWeek,
                    body.StartDate,
                    body.EndDate,
                    body.DailyStartMinutes,
                    body.DailyEndMinutes,
                    body.PhotoFileId), ct);
                return Results.Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "RecurringVisitor"));

        // POST /{id}/revoke — resident revokes their recurring visitor (Req 25.2). Create grant.
        group.MapPost("/{id:guid}/revoke", async (
                Guid id,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.RevokeRecurringVisitorAsync(id, ct);
                return Results.Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "RecurringVisitor"));

        // POST /{id}/extend — extend the validity (new end date) and reactivate (Req 25.2). Create grant.
        group.MapPost("/{id:guid}/extend", async (
                Guid id,
                ExtendRecurringVisitorBody body,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.ExtendRecurringVisitorAsync(id, body.EndDate, ct);
                return Results.Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "RecurringVisitor"));

        // GET — paginated/filter(by status/category)/sort list (Req 25.2).
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? status,
                string? category,
                Guid? hostUnitId,
                bool? active,
                int? page,
                int? pageSize,
                IGateService gate,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                AddEquals(filters, "status", status);
                AddEquals(filters, "category", category);
                AddEquals(filters, "hostUnitId", hostUnitId);

                var data = await gate.ListRecurringVisitorsAsync(BuildParameters(page, pageSize, search, sort, filters), active, ct);
                return Results.Ok(ApiResult<PagedData<RecurringVisitorDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "RecurringVisitor"));

        // GET /{id} — fetch one, tenant/self-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IGateService gate,
                CancellationToken ct) =>
            {
                var dto = await gate.GetRecurringVisitorAsync(id, ct)
                    ?? throw new NotFoundException("Recurring visitor not found.");
                return Results.Ok(ApiResult<RecurringVisitorDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "RecurringVisitor"));
    }

    // --- Shared helpers --------------------------------------------------------------------------

    /// <summary>Add an equals filter term only when the value is present (keeps the allow-list honest).</summary>
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

    private static ListQueryParameters BuildParameters(
        int? page, int? pageSize, string? search, string? sort, List<FilterTerm> filters) =>
        new()
        {
            Page = page,
            PageSize = pageSize,
            Search = string.IsNullOrWhiteSpace(search) ? null : search,
            Filters = filters,
            Sort = EndpointSort.Parse(sort),
        };

    // --- Request bodies (validated via DataAnnotationsValidationFilter) --------------------------

    public sealed class CreateVisitPassBody
    {
        [Required(ErrorMessage = "hostUnitId is required")]
        public Guid HostUnitId { get; init; }

        public Guid? HostResidentId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "visitorName is required")]
        [MaxLength(256)]
        public string? VisitorName { get; init; }

        public DateTime? ValidFromUtc { get; init; }

        public DateTime? ValidUntilUtc { get; init; }

        public int? MaxUses { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [EmailAddress(ErrorMessage = "email must be a valid email address")]
        [MaxLength(256)]
        public string? Email { get; init; }

        [MaxLength(50)]
        public string? VehicleNumber { get; init; }

        [MaxLength(100)]
        public string? VehicleType { get; init; }

        [Range(1, 100, ErrorMessage = "numberOfPersons must be between 1 and 100")]
        public int? NumberOfPersons { get; init; }

        [MaxLength(200)]
        public string? WhomToMeet { get; init; }

        public DateTime? VisitDateUtc { get; init; }

        public Guid? PhotoFileId { get; init; }
    }

    public sealed class VerifyVisitPassBody
    {
        // Exactly one of QrToken/Otp identifies the pass; the service validates presence.
        [MaxLength(512)]
        public string? QrToken { get; init; }

        [MaxLength(32)]
        public string? Otp { get; init; }
    }

    public sealed class SharePassBody
    {
        public bool Sms { get; init; }
        public bool WhatsApp { get; init; }
        public bool Email { get; init; }

        [MaxLength(50)]
        public string? ToPhone { get; init; }

        [MaxLength(256)]
        public string? ToEmail { get; init; }
    }

    public sealed class ScanAccessBody
    {
        [MaxLength(512)]
        public string? QrToken { get; init; }

        [MaxLength(32)]
        public string? Otp { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }
    }

    public sealed class RegisterWalkInBody
    {
        [Required(ErrorMessage = "hostUnitId is required")]
        public Guid HostUnitId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [EmailAddress(ErrorMessage = "email must be a valid email address")]
        [MaxLength(256)]
        public string? Email { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(100)]
        public string? Category { get; init; }

        public Guid? PhotoFileId { get; init; }

        [MaxLength(50)]
        public string? VehicleNumber { get; init; }

        [MaxLength(256)]
        public string? GateLocation { get; init; }

        [MaxLength(100)]
        public string? IdProofType { get; init; }

        [MaxLength(100)]
        public string? IdProofNumber { get; init; }

        [MaxLength(500)]
        public string? Purpose { get; init; }

        [MaxLength(200)]
        public string? WhomToMeet { get; init; }

        [Range(1, 10080, ErrorMessage = "expectedDurationMinutes must be between 1 and 10080")]
        public int? ExpectedDurationMinutes { get; init; }
    }

    public sealed class ApproveWalkInBody
    {
        [Required(ErrorMessage = "approvingResidentId is required")]
        public Guid ApprovingResidentId { get; init; }
    }

    public sealed class RejectWalkInBody
    {
        [Required(ErrorMessage = "rejectingResidentId is required")]
        public Guid RejectingResidentId { get; init; }

        [MaxLength(1000)]
        public string? Reason { get; init; }
    }

    public sealed class RecordEntryBody
    {
        public Guid? VisitorId { get; init; }

        public Guid? VisitPassId { get; init; }

        public Guid? RecurringVisitorId { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }

        [MaxLength(50)]
        public string? VehicleNumber { get; init; }
    }

    public sealed class RecordExitBody
    {
        public Guid? VisitorId { get; init; }

        public Guid? VisitPassId { get; init; }

        public Guid? RecurringVisitorId { get; init; }

        public Guid? GateId { get; init; }

        [MaxLength(256)]
        public string? GateLabel { get; init; }
    }

    public sealed class CreateRecurringVisitorBody
    {
        [Required(ErrorMessage = "hostUnitId is required")]
        public Guid HostUnitId { get; init; }

        [Required(ErrorMessage = "hostResidentId is required")]
        public Guid HostResidentId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "name is required")]
        [MaxLength(256)]
        public string? Name { get; init; }

        [MaxLength(50)]
        public string? Phone { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "category is required")]
        [MaxLength(100)]
        public string? Category { get; init; }

        [Range(1, 127, ErrorMessage = "daysOfWeek must be a 7-bit day mask (1-127)")]
        public int DaysOfWeek { get; init; }

        [Required(ErrorMessage = "startDate is required")]
        public DateTime StartDate { get; init; }

        [Required(ErrorMessage = "endDate is required")]
        public DateTime EndDate { get; init; }

        public int? DailyStartMinutes { get; init; }

        public int? DailyEndMinutes { get; init; }

        public Guid? PhotoFileId { get; init; }
    }

    public sealed class ExtendRecurringVisitorBody
    {
        [Required(ErrorMessage = "endDate is required")]
        public DateTime EndDate { get; init; }
    }
}
