using App.Application.Audit;
using App.Application.Community;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Community;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Community;

/// <summary>
/// EF-backed <see cref="IUnitService"/> (Req 14.1–14.4, 14.6, 22.1, 22.4).
///
/// <para><b>Configurable type/status (Req 14.2, 14.3).</b> <c>UnitType</c>/<c>UnitStatus</c> are
/// validated at runtime against the editable <c>Unit_Type</c>/<c>Unit_Status</c> master-data lists
/// resolved for the unit's own community (so a per-community override applies) — not frozen enums,
/// so an operator can add/retire a type or status without a code change.</para>
///
/// <para><b>Concurrency-safe uniqueness (Req 14.4, 22.1).</b> A duplicate <c>(CommunityId,
/// UnitNumber)</c> is NOT pre-checked with a read-then-write (which would race two concurrent
/// creates past the gap and let both insert). The write relies on the DB unique index
/// (UX_Unit_CommunityId_UnitNumber) as the single source of truth and translates the race loser's
/// violation into a CONFLICT via <see cref="UniqueConstraintGuard"/>. Written to stay correct when
/// two requests create the same number at the same time: exactly one wins, the other gets CONFLICT.</para>
///
/// <para><b>Optimistic concurrency on edit (Req 22.4).</b> The inherited <c>RowVersion</c> token is
/// checked on <c>SaveChanges</c>; a conflicting concurrent update raises
/// <see cref="DbUpdateConcurrencyException"/>, which this service maps to a <see cref="ConflictException"/>
/// (CONFLICT / 409) so a lost update is reported rather than silently overwriting.</para>
///
/// <para><b>Audit + tenant scope.</b> Create/update/delete each append an <see cref="AuditEntry"/>
/// (Req 6.1); reads are tenant-scoped by the global filter (Req 5.2) and use <c>AsNoTracking</c>;
/// delete is a soft delete that stays traceable (Req 14.6).</para>
/// </summary>
public sealed class UnitService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IUnitService
{
    private const string DuplicateMessage = "A unit with the same number already exists in this community.";

    // Allow-list for the list endpoint (Req 14.5). unitStatus is both sortable and filterable so the
    // "units in this community with status X" query hits the (CommunityId, UnitStatus) index at scale.
    // No arbitrary client fields reach the query (steering: no hardcoding, injection-safe).
    private static readonly ListQueryConfiguration<Unit> QueryConfig =
        new ListQueryConfiguration<Unit>()
            .WithDefaultSort(u => u.UnitNumber)
            .Sortable("unitNumber", u => u.UnitNumber)
            .Sortable("unitType", u => u.UnitType)
            .Sortable("unitStatus", u => u.UnitStatus)
            .Sortable("floor", u => u.Floor)
            .Filterable("unitType", u => u.UnitType)
            .Filterable("unitStatus", u => u.UnitStatus)
            .Filterable("floor", u => u.Floor)
            .Filterable("hierarchyNodeId", u => u.HierarchyNodeId)
            .Filterable("communityId", u => u.CommunityId)
            .Searchable(u => u.UnitNumber);

    public async Task<UnitDto> CreateAsync(CreateUnitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireUnitNumber(request.UnitNumber);
        await RequireValidUnitTypeAsync(request.UnitType, request.CommunityId, cancellationToken);
        await RequireValidUnitStatusAsync(request.UnitStatus, request.CommunityId, cancellationToken);

        if (request.HierarchyNodeId is Guid nodeId)
        {
            await RequireNodeInSameCommunityAsync(nodeId, request.CommunityId, cancellationToken);
        }

        var unit = new Unit
        {
            CommunityId = request.CommunityId,
            HierarchyNodeId = request.HierarchyNodeId,
            UnitNumber = request.UnitNumber.Trim(),
            UnitType = request.UnitType.Trim(),
            Area = request.Area,
            Floor = request.Floor?.Trim(),
            Bedrooms = request.Bedrooms,
            UnitStatus = request.UnitStatus.Trim(),
            CustomAttributes = ToStore(request.CustomAttributes),
        };

        db.Units.Add(unit);

        // Concurrency-safe: let the DB unique index decide the winner (no read-then-write pre-check).
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateMessage);

        await audit.AppendAsync(
            new AuditEntry("Create", "Unit", unit.Id.ToString(), unit.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(unit);
    }

    public async Task<UnitDto> UpdateAsync(Guid unitId, UpdateUnitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken)
            ?? throw new NotFoundException("Unit not found.");

        if (request.UnitNumber is not null)
        {
            RequireUnitNumber(request.UnitNumber);
            unit.UnitNumber = request.UnitNumber.Trim();
        }

        if (request.UnitType is not null)
        {
            await RequireValidUnitTypeAsync(request.UnitType, unit.CommunityId, cancellationToken);
            unit.UnitType = request.UnitType.Trim();
        }

        if (request.UnitStatus is not null)
        {
            await RequireValidUnitStatusAsync(request.UnitStatus, unit.CommunityId, cancellationToken);
            unit.UnitStatus = request.UnitStatus.Trim();
        }

        if (request.UpdateHierarchyNode)
        {
            if (request.HierarchyNodeId is Guid newNodeId)
            {
                await RequireNodeInSameCommunityAsync(newNodeId, unit.CommunityId, cancellationToken);
            }

            unit.HierarchyNodeId = request.HierarchyNodeId; // null => unlink
        }

        if (request.UpdateArea)
        {
            unit.Area = request.Area;
        }

        if (request.UpdateFloor)
        {
            unit.Floor = request.Floor?.Trim();
        }

        if (request.UpdateBedrooms)
        {
            unit.Bedrooms = request.Bedrooms;
        }

        if (request.CustomAttributes is not null)
        {
            unit.CustomAttributes = ToStore(request.CustomAttributes);
        }

        // A renamed unit can collide with an existing number → CONFLICT via the DB unique index.
        // The optimistic-concurrency token mismatch (Req 22.4) — another request updated this row
        // first — is mapped to CONFLICT centrally in AppDbContext.SaveChangesAsync, so there is no
        // per-service DbUpdateConcurrencyException catch here.
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateMessage);

        await audit.AppendAsync(
            new AuditEntry("Update", "Unit", unit.Id.ToString(), unit.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(unit);
    }

    public async Task DeleteAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken)
            ?? throw new NotFoundException("Unit not found.");

        // Remove is converted to a soft delete by the audit interceptor; the archived unit stays
        // traceable (Req 14.6).
        db.Units.Remove(unit);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Unit", unit.Id.ToString(), unit.CommunityId, Detail: null),
            cancellationToken);
    }

    public async Task<UnitDto?> GetAsync(Guid unitId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Unit> q = db.Units.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit (Req 14.6)
        }

        var unit = await q.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        return unit is null ? null : ToDto(unit);
    }

    public Task<PagedData<UnitDto>> ListAsync(ListQueryParameters parameters, bool availableForOwner = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var source = db.Units.AsNoTracking();

        if (availableForOwner)
        {
            source = source.Where(u => !db.HouseholdMembers
                .Where(h => h.UnitId == u.Id && h.EndDate == null)
                .Join(db.Residents, h => h.ResidentId, r => r.Id, (h, r) => r.ResidentType)
                .Any(t => t == "owner"));
        }

        return source.ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    private static UnitDto ToDto(Unit u) =>
        new(u.Id, u.CommunityId, u.HierarchyNodeId, u.UnitNumber, u.UnitType, u.Area, u.Floor,
            u.Bedrooms, u.UnitStatus,
            new Dictionary<string, string>(u.CustomAttributes), u.IsDeleted);

    private static Dictionary<string, string> ToStore(IReadOnlyDictionary<string, string>? attributes) =>
        attributes is null ? [] : new Dictionary<string, string>(attributes);

    private static void RequireUnitNumber(string? unitNumber)
    {
        if (string.IsNullOrWhiteSpace(unitNumber))
        {
            throw new ValidationException("unitNumber", "A unit number is required.");
        }
    }

    private async Task RequireValidUnitTypeAsync(string? unitType, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitType))
        {
            throw new ValidationException("unitType", "A unit type is required.");
        }

        // Validate against the configurable Unit_Type list resolved for this community (Req 14.2) —
        // not a frozen enum, so types stay editable per community.
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.UnitType, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, unitType.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("unitType", $"'{unitType}' is not a valid unit type.");
        }
    }

    private async Task RequireValidUnitStatusAsync(string? unitStatus, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(unitStatus))
        {
            throw new ValidationException("unitStatus", "A unit status is required.");
        }

        // Validate against the configurable Unit_Status list resolved for this community (Req 14.3).
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.UnitStatus, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, unitStatus.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("unitStatus", $"'{unitStatus}' is not a valid unit status.");
        }
    }

    private async Task RequireNodeInSameCommunityAsync(Guid nodeId, Guid communityId, CancellationToken cancellationToken)
    {
        // Tenant-scoped read: the global filter already hides foreign-tenant nodes. Load the node's
        // CommunityId and reject a cross-community link with a validation error (same convention as
        // HierarchyService's same-community parent check).
        var nodeCommunityId = await db.HierarchyNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(n => (Guid?)n.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (nodeCommunityId is null || nodeCommunityId.Value != communityId)
        {
            throw new ValidationException("hierarchyNodeId", "The hierarchy node must belong to the same community.");
        }
    }
}
