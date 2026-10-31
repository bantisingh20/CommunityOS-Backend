using App.Domain.Common;

namespace App.Domain.Gate;


public class VisitPass : TenantOwnedEntity, IUnitScoped
{
   
    public Guid? UnitId { get; set; }

    /// <summary>The host unit being visited (Req 23.1). Alias over <see cref="UnitId"/> for intent.</summary>
    public Guid HostUnitId
    {
        get => UnitId ?? Guid.Empty;
        set => UnitId = value;
    }

    /// <summary>Optional inviting resident (Req 23.1). The resident who created the pass for their own unit.</summary>
    public Guid? HostResidentId { get; set; }

    /// <summary>The invited visitor's name (Req 23.1). Required.</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>Optional visitor contact phone captured on the invite.</summary>
    public string? Phone { get; set; }

    public string? Email { get; set; }

    /// <summary>Optional captured vehicle number.</summary>
    public string? VehicleNumber { get; set; }

    /// <summary>Optional vehicle type code resolved against the editable <c>Vehicle_Type</c> list.</summary>
    public string? VehicleType { get; set; }

    /// <summary>Optional number of people expected under this pass. Defaults to 1 when set.</summary>
    public int? NumberOfPersons { get; set; }

    /// <summary>Optional free-text person / flat the visitor is here to meet (beyond the host unit).</summary>
    public string? WhomToMeet { get; set; }

    /// <summary>Optional expected date of visit (the day the visitor is expected).</summary>
    public DateTime? VisitDateUtc { get; set; }

    /// <summary>Optional File_Service reference to a captured visitor photo.</summary>
    public Guid? PhotoFileId { get; set; }

    /// <summary>
    /// Opaque, community-unique QR code the gate scans (Req 23.1). High-entropy random string, not
    /// derived from pass data — it leaks nothing and is the indexed lookup key at the gate.
    /// </summary>
    public string QrToken { get; set; } = string.Empty;

    /// <summary>
    /// Opaque, high-entropy token for the PUBLIC shareable pass link (view-only). Distinct from
    /// <see cref="QrToken"/> so the gate-scan secret is never exposed in a shared URL/SMS. Anyone
    /// with this token can VIEW the pass (name, photo, QR for the gate, window) but it is not itself
    /// a gate credential.
    /// </summary>
    public string PublicToken { get; set; } = string.Empty;

    
    public string OtpHash { get; set; } = string.Empty;

    /// <summary>Start of the validity window (Req 23.1). The pass is not valid before this time.</summary>
    public DateTime ValidFromUtc { get; set; }

    /// <summary>End of the validity window (Req 23.1, 23.2). The pass expires at this time.</summary>
    public DateTime ValidUntilUtc { get; set; }

    /// <summary>Maximum number of accepted verifications (Req 23.4). Defaults to 1 (single-use).</summary>
    public int MaxUses { get; set; } = 1;

    /// <summary>Accepted verifications so far (Req 23.3, 23.4). Incremented concurrency-safely on accept.</summary>
    public int UseCount { get; set; }

   
    public string Status { get; set; } = VisitPassStatuses.Active;
}

/// <summary>
/// Well-known default <c>Visit_Pass_Status</c> codes (Req 23.4). These are the seeded master-data
/// codes, kept as constants so the service sets/compares status without a magic string. They are
/// <b>data</b>, not a behavioural enum — the status list is editable; this is only a convenience for
/// the known codes.
/// </summary>
public static class VisitPassStatuses
{
    /// <summary>Initial status on create (Req 23.1): the pass can be verified within its window.</summary>
    public const string Active = "active";

    /// <summary>Uses exhausted (<c>UseCount</c> reached <c>MaxUses</c>, Req 23.4).</summary>
    public const string Used = "used";

    /// <summary>Past its valid-until time (Req 23.2). Derived from the window; a convenience code.</summary>
    public const string Expired = "expired";

    /// <summary>Cancelled by the host/admin before use (Req 23.4).</summary>
    public const string Revoked = "revoked";
}
