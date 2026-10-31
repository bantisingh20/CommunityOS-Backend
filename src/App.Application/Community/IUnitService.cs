using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.Community;

/// <summary>
/// Unit management (Req 14.1–14.4, 14.6). Create/edit/soft-delete/get a unit, with its number,
/// type, area, floor, bedrooms, status, free-form custom attributes and owning community.
///
/// <para><b>Configurable type/status (Req 14.2, 14.3).</b> <c>UnitType</c> is validated against the
/// editable <c>Unit_Type</c> list and <c>UnitStatus</c> against <c>Unit_Status</c> — resolved at
/// runtime, not frozen enums.</para>
///
/// <para><b>Concurrency-safe uniqueness (Req 14.4, 22.1).</b> A duplicate <c>(CommunityId,
/// UnitNumber)</c> is rejected by the DB unique index and surfaced as a CONFLICT — there is no
/// read-then-write pre-check, so two concurrent creates of the same number cannot both succeed.
/// The <c>RowVersion</c> optimistic-concurrency token guards edits (Req 22.4): a conflicting
/// concurrent update maps to CONFLICT.</para>
///
/// <para>Reads are tenant-scoped (Req 5.2); mutations are audited (Req 6.1); delete is a soft delete
/// that stays traceable (Req 14.6).</para>
/// </summary>
public interface IUnitService
{
    /// <summary>
    /// Create a unit (Req 14.1). Validates the type/status against the configurable lists
    /// (Req 14.2, 14.3) and, when a hierarchy node is supplied, that it belongs to the same
    /// community. A duplicate <c>(CommunityId, UnitNumber)</c> → CONFLICT (Req 14.4, 22.1).
    /// </summary>
    Task<UnitDto> CreateAsync(CreateUnitRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit a unit. Changed type/status are re-validated; a changed hierarchy node is re-checked for
    /// same-community. A duplicate unit number → CONFLICT; a concurrent update losing the optimistic
    /// concurrency check → CONFLICT (Req 22.4). Unknown id → not found.
    /// </summary>
    Task<UnitDto> UpdateAsync(Guid unitId, UpdateUnitRequest request, CancellationToken cancellationToken = default);

    /// <summary>Soft-delete (archive) a unit (Req 14.6). The row is retained and still retrievable with <c>includeArchived</c>.</summary>
    Task DeleteAsync(Guid unitId, CancellationToken cancellationToken = default);

    /// <summary>Fetch a single unit, tenant-scoped. When <paramref name="includeArchived"/> is true a soft-deleted unit is returned (Req 14.6).</summary>
    Task<UnitDto?> GetAsync(Guid unitId, bool includeArchived = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated/filter(by status)/sort/search list of (non-archived) units (Req 14.5, 9.3). The
    /// result is scoped to the caller's authorized communities by the global tenant query filter;
    /// the <c>(CommunityId, UnitStatus)</c> index backs the common "units in this community with
    /// status X" filter at scale.
    /// </summary>
    Task<PagedData<UnitDto>> ListAsync(ListQueryParameters parameters, bool availableForOwner = false, CancellationToken cancellationToken = default);
}
