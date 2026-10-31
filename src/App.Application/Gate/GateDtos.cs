namespace App.Application.Gate;

public sealed record VisitPassDto(
    Guid Id,
    Guid CommunityId,
    Guid HostUnitId,
    Guid? HostResidentId,
    string VisitorName,
    string? Phone,
    string? Email,
    string? VehicleNumber,
    string? VehicleType,
    int? NumberOfPersons,
    string? WhomToMeet,
    DateTime? VisitDateUtc,
    Guid? PhotoFileId,
    string QrToken,
    DateTime ValidFromUtc,
    DateTime ValidUntilUtc,
    int MaxUses,
    int UseCount,
    string Status,
    string? HostUnitNumber = null,
    string? OrganizationName = null,
    string PublicToken = "");

public sealed record CreateVisitPassResult(VisitPassDto Pass, string Otp);

/// <summary>
/// The safe, no-login view of a pass for the PUBLIC shareable page. Deliberately excludes PII
/// (phone/email) and the OTP. `PhotoDataUrl` is the visitor photo inlined as a data URL so the
/// anonymous page needs no authorized file call. `IsValidNow` reflects the window/status at fetch.
/// </summary>
public sealed record PublicVisitPassDto(
    string VisitorName,
    string? PhotoDataUrl,
    string QrToken,
    DateTime ValidFromUtc,
    DateTime ValidUntilUtc,
    string Status,
    bool IsValidNow,
    int? NumberOfPersons,
    string? WhomToMeet,
    string? VehicleNumber,
    string? HostUnitNumber,
    string? CommunityName,
    string? OrganizationName);

/// <summary>Which channels to send the public pass link on (admin-triggered share). At least one.</summary>
public sealed record SharePassRequest(
    bool Sms = false,
    bool WhatsApp = false,
    bool Email = false,
    string? ToPhone = null,
    string? ToEmail = null);

public sealed record SharePassResult(string PublicUrl, bool SmsQueued, bool WhatsAppQueued, bool EmailQueued);

public sealed record VisitorDto(
    Guid Id,
    Guid CommunityId,
    Guid HostUnitId,
    string Name,
    string? Phone,
    string? Email,
    string Category,
    Guid? PhotoFileId,
    string? VehicleNumber,
    string? GateLocation,
    string? IdProofType,
    string? IdProofNumber,
    string? Purpose,
    string? WhomToMeet,
    int? ExpectedDurationMinutes,
    string Status,
    Guid? ApprovedByResidentId,
    DateTime? ApprovalDecisionAtUtc,
    bool IsAdmittable,
    string? QrToken = null,
    string? HostUnitNumber = null,
    string? OrganizationName = null);

public sealed record VerifyVisitPassResult(
    bool Accepted,
    string? RejectionReason,
    VisitPassDto? Pass);


public sealed record CreateVisitPassRequest(
    Guid HostUnitId,
    Guid? HostResidentId,
    string VisitorName,
    DateTime? ValidFromUtc,
    DateTime? ValidUntilUtc,
    int? MaxUses,
    string? Phone = null,
    string? Email = null,
    string? VehicleNumber = null,
    string? VehicleType = null,
    int? NumberOfPersons = null,
    string? WhomToMeet = null,
    DateTime? VisitDateUtc = null,
    Guid? PhotoFileId = null);

public sealed record VerifyVisitPassRequest(
    string? QrToken,
    string? Otp);

public sealed record RegisterWalkInRequest(
    Guid HostUnitId,
    string Name,
    string? Phone,
    string Category,
    Guid? PhotoFileId,
    string? VehicleNumber,
    string? GateLocation,
    string? Email = null,
    string? IdProofType = null,
    string? IdProofNumber = null,
    string? Purpose = null,
    string? WhomToMeet = null,
    int? ExpectedDurationMinutes = null);

public sealed record RegisterWalkInResult(VisitorDto Visitor, bool AutoApproved, string? Otp);

/// <summary>Reject a pending walk-in visitor (Req 24.4). The reason is optional free text recorded in the audit trail.</summary>
/// <param name="Reason">Optional reason for the rejection.</param>
public sealed record RejectWalkInRequest(string? Reason);

public sealed record ScanAccessRequest(
    string? QrToken,
    string? Otp,
    Guid? GateId,
    string? GateLabel);

public sealed record ScanAccessResult(
    string Action,
    string AuthorizationType,
    string Name,
    string? Reason);

public static class ScanActions
{
    public const string CheckedIn = "checked_in";
    public const string CheckedOut = "checked_out";
    public const string Closed = "closed";
    public const string Rejected = "rejected";
}

public sealed record VehicleLookupHostResident(string Name, string? Phone, string ResidentType);

public sealed record VehicleLookupMatch(
    string MatchType,
    string? VehicleType,
    string PersonName,
    string? PersonPhone,
    string? PersonEmail,
    Guid? UnitId,
    string? UnitNumber,
    string? Status,
    IReadOnlyList<VehicleLookupHostResident> HostResidents);

public sealed record VehicleLookupResult(string Plate, IReadOnlyList<VehicleLookupMatch> Matches);

public static class VehicleMatchTypes
{
    public const string Resident = "resident";
    public const string VisitPass = "visit_pass";
    public const string WalkIn = "walk_in";
}

public sealed record RecurringVisitorDto(
    Guid Id,
    Guid CommunityId,
    Guid HostUnitId,
    Guid HostResidentId,
    string Name,
    string? Phone,
    string Category,
    int DaysOfWeek,
    DateTime StartDate,
    DateTime EndDate,
    int? DailyStartMinutes,
    int? DailyEndMinutes,
    string Status,
    Guid? PhotoFileId = null);

public sealed record CreateRecurringVisitorRequest(
    Guid HostUnitId,
    Guid HostResidentId,
    string Name,
    string? Phone,
    string Category,
    int DaysOfWeek,
    DateTime StartDate,
    DateTime EndDate,
    int? DailyStartMinutes,
    int? DailyEndMinutes,
    Guid? PhotoFileId = null);

public sealed record EntryExitEventDto(
    Guid Id,
    Guid CommunityId,
    Guid? GateId,
    string? GateLabel,
    Guid? DestinationUnitId,
    Guid? VisitorId,
    Guid? VisitPassId,
    Guid? RecurringVisitorId,
    string VisitorName,
    string Category,
    string? VehicleNumber,
    DateTime EntryTsUtc,
    DateTime? ExitTsUtc,
    string? HostUnitNumber = null,
    string? VisitorPhone = null,
    Guid? PhotoFileId = null,
    string? WhomToMeet = null);

public sealed record RecordEntryRequest(
    Guid? VisitorId,
    Guid? VisitPassId,
    Guid? RecurringVisitorId,
    Guid? GateId,
    string? GateLabel,
    string? VehicleNumber);

public sealed record RecordExitRequest(
    Guid? VisitorId,
    Guid? VisitPassId,
    Guid? RecurringVisitorId,
    Guid? GateId,
    string? GateLabel);

public enum VisitPassValidityUnit
{
    Days,
    Hours,
}

public readonly record struct VisitPassValidity(int Amount, VisitPassValidityUnit Unit)
{
    public DateTime Apply(DateTime from) =>
        Unit == VisitPassValidityUnit.Hours ? from.AddHours(Amount) : from.AddDays(Amount);

    public string UnitCode => Unit == VisitPassValidityUnit.Hours ? "hours" : "days";

    public string Describe()
    {
        var noun = Unit == VisitPassValidityUnit.Hours
            ? (Amount == 1 ? "hour" : "hours")
            : (Amount == 1 ? "day" : "days");
        return $"{Amount} {noun}";
    }
}
