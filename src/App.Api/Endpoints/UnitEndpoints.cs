using System.ComponentModel.DataAnnotations;
using App.Api.Security;
using App.Api.Validation;
using App.Application.Community;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;

namespace App.Api.Endpoints;

/// <summary>
/// HTTP surface over <see cref="IUnitService"/> (Task 11.3) — units within a community (Req 14.1–14.6).
/// Every endpoint uses the <see cref="ApiResult{T}"/> envelope (Req 9.1), carries a
/// <see cref="RequirePermissionAttribute"/> on the <c>Unit</c> resource type for its action
/// (Req 4.1), validates write bodies (Req 9.2), applies the "write" rate-limit policy to mutations
/// (Req 65.5), and the list is paginated/filter(by status)/sort/search through the shared query
/// infra (Req 14.5, 9.3). The list is <b>scoped to the caller's authorized communities</b> by the EF
/// global tenant query filter — not a bespoke predicate here — and the status filter rides the
/// <c>(CommunityId, UnitStatus)</c> index. Duplicate unit-number (CONFLICT), invalid type/status
/// (VALIDATION_ERROR) and concurrency (CONFLICT) are surfaced by the service / data layer.
/// </summary>
public static class UnitEndpoints
{
    public static IEndpointRouteBuilder MapUnitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/units");

        // POST /api/v1/units — create (Req 14.1). Duplicate number → CONFLICT; invalid type/status → VALIDATION_ERROR.
        group.MapPost("/", async (
                CreateUnitBody body,
                IUnitService units,
                CancellationToken ct) =>
            {
                var dto = await units.CreateAsync(new CreateUnitRequest(
                    body.CommunityId,
                    body.HierarchyNodeId,
                    body.UnitNumber!,
                    body.UnitType!,
                    body.Area,
                    body.Floor,
                    body.Bedrooms,
                    body.UnitStatus!,
                    body.CustomAttributes), ct);
                return Results.Ok(ApiResult<UnitDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Unit"));

        // GET /api/v1/units — paginated/filter(by status)/sort/search list, scoped to authorized
        // communities by the tenant filter (Req 14.5). Non-archived only.
        group.MapGet("/", async (
                string? search,
                string? sort,
                string? unitStatus,
                string? unitType,
                string? floor,
                Guid? hierarchyNodeId,
                Guid? communityId,
                bool? availableForOwner,
                int? page,
                int? pageSize,
                IUnitService units,
                CancellationToken ct) =>
            {
                var filters = new List<FilterTerm>();
                if (!string.IsNullOrWhiteSpace(unitStatus))
                {
                    filters.Add(new FilterTerm("unitStatus", FilterOperator.Equals, unitStatus));
                }
                if (!string.IsNullOrWhiteSpace(unitType))
                {
                    filters.Add(new FilterTerm("unitType", FilterOperator.Equals, unitType));
                }
                if (!string.IsNullOrWhiteSpace(floor))
                {
                    filters.Add(new FilterTerm("floor", FilterOperator.Equals, floor));
                }
                if (hierarchyNodeId is not null)
                {
                    filters.Add(new FilterTerm("hierarchyNodeId", FilterOperator.Equals, hierarchyNodeId.Value.ToString()));
                }
                if (communityId is not null)
                {
                    filters.Add(new FilterTerm("communityId", FilterOperator.Equals, communityId.Value.ToString()));
                }

                var parameters = new ListQueryParameters
                {
                    Page = page,
                    PageSize = pageSize,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search,
                    Filters = filters,
                    Sort = EndpointSort.Parse(sort),
                };

                var data = await units.ListAsync(parameters, availableForOwner ?? false, ct);
                return Results.Ok(ApiResult<PagedData<UnitDto>>.Ok(data, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Unit"));

        // GET /api/v1/units/{id} — fetch one, tenant-scoped. Unknown/foreign id → NOT_FOUND.
        group.MapGet("/{id:guid}", async (
                Guid id,
                IUnitService units,
                CancellationToken ct) =>
            {
                var dto = await units.GetAsync(id, includeArchived: false, ct)
                    ?? throw new NotFoundException("Unit not found.");
                return Results.Ok(ApiResult<UnitDto>.Ok(dto, string.Empty));
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.View, "Unit"));

        // PUT /api/v1/units/{id} — edit. Duplicate number / concurrency → CONFLICT; unknown → NOT_FOUND.
        group.MapPut("/{id:guid}", async (
                Guid id,
                UpdateUnitBody body,
                IUnitService units,
                CancellationToken ct) =>
            {
                var dto = await units.UpdateAsync(id, new UpdateUnitRequest(
                    body.HierarchyNodeId,
                    body.UpdateHierarchyNode,
                    body.UnitNumber,
                    body.UnitType,
                    body.Area,
                    body.UpdateArea,
                    body.Floor,
                    body.UpdateFloor,
                    body.Bedrooms,
                    body.UpdateBedrooms,
                    body.UnitStatus,
                    body.CustomAttributes), ct);
                return Results.Ok(ApiResult<UnitDto>.Ok(dto, string.Empty));
            })
            .AddEndpointFilter<DataAnnotationsValidationFilter>()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Edit, "Unit"));

        // DELETE /api/v1/units/{id} — soft-delete (Req 14.6).
        group.MapDelete("/{id:guid}", async (
                Guid id,
                IUnitService units,
                CancellationToken ct) =>
            {
                await units.DeleteAsync(id, ct);
                return Results.Ok(ApiResult<DeletedResponse>.Ok(new DeletedResponse(id), string.Empty));
            })
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Delete, "Unit"));

        // POST /api/v1/units/import — bulk CSV import into a community (Req 21.1, 21.2, 21.4, 21.5).
        // Multipart CSV upload (the common admin format). Create-gated on Unit; the service validates
        // each row, creates the valid ones and returns a per-row rejection list (partial success —
        // one bad row never aborts the batch). An oversized file → PAYLOAD_TOO_LARGE before
        // processing. The result rides the standard JSON envelope.
        group.MapPost("/import", async (
                IFormFile file,
                Guid communityId,
                IUnitBulkService bulk,
                CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                {
                    throw new App.Application.Exceptions.ValidationException("file", "a non-empty CSV file is required");
                }

                using var reader = new StreamReader(file.OpenReadStream());
                var csv = await reader.ReadToEndAsync(ct);

                var result = await bulk.ImportUnitsAsync(communityId, csv, ct);
                return Results.Ok(ApiResult<BulkImportResult>.Ok(result, string.Empty));
            })
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitingExtensions.WritePolicy)
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Create, "Unit"));

        // GET /api/v1/units/export — bulk CSV export (Req 21.3, 21.4, 21.5, 66.6). Export-gated; the
        // service additionally enforces Export⊆View and the tenant filter so the file can never
        // exceed the caller's viewable, authorized scope, and caps the row count. The CSV is streamed
        // as a file download — intentionally NOT wrapped in the JSON envelope (same pattern as the
        // File download).
        group.MapGet("/export", async (
                IUnitBulkService bulk,
                CancellationToken ct) =>
            {
                var export = await bulk.ExportUnitsAsync(ct);
                return Results.File(export.Content, export.ContentType, export.FileName);
            })
            .WithMetadata(new RequirePermissionAttribute(PermissionAction.Export, "Unit"));

        return app;
    }

    public sealed class CreateUnitBody
    {
        [Required(ErrorMessage = "communityId is required")]
        public Guid CommunityId { get; init; }

        public Guid? HierarchyNodeId { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "unitNumber is required")]
        [MaxLength(100)]
        public string? UnitNumber { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "unitType is required")]
        [MaxLength(100)]
        public string? UnitType { get; init; }

        public decimal? Area { get; init; }

        [MaxLength(100)]
        public string? Floor { get; init; }

        public int? Bedrooms { get; init; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "unitStatus is required")]
        [MaxLength(100)]
        public string? UnitStatus { get; init; }

        public IReadOnlyDictionary<string, string>? CustomAttributes { get; init; }
    }

    public sealed class UpdateUnitBody
    {
        public Guid? HierarchyNodeId { get; init; }

        // When true the HierarchyNodeId above is applied (allowing null = unlink); when false untouched.
        public bool UpdateHierarchyNode { get; init; }

        [MaxLength(100)]
        public string? UnitNumber { get; init; }

        [MaxLength(100)]
        public string? UnitType { get; init; }

        public decimal? Area { get; init; }

        // When true Area is applied (allowing null = clear); when false untouched.
        public bool UpdateArea { get; init; }

        [MaxLength(100)]
        public string? Floor { get; init; }

        // When true Floor is applied (allowing null = clear); when false untouched.
        public bool UpdateFloor { get; init; }

        public int? Bedrooms { get; init; }

        // When true Bedrooms is applied (allowing null = clear); when false untouched.
        public bool UpdateBedrooms { get; init; }

        [MaxLength(100)]
        public string? UnitStatus { get; init; }

        public IReadOnlyDictionary<string, string>? CustomAttributes { get; init; }
    }
}
