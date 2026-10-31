namespace App.Application.MyTasks;

public static class MyTaskTypes
{
    public const string WalkInApproval = "walkin_approval";
    public const string ParcelPickup = "parcel_pickup";
    public const string MaintenanceDue = "maintenance_due";
    public const string KycPending = "kyc_pending";
    public const string ApprovedWalkInCheckIn = "approved_walkin_checkin";
    public const string ActiveSos = "active_sos";
    public const string ResidentToVerify = "resident_verify";
    public const string OpenTicket = "open_ticket";
    public const string OverdueBill = "overdue_bill";
    public const string PendingPaymentApproval = "pending_payment_approval";
}

public sealed record MyTaskSummaryItem(
    string Type,
    string Label,
    int Count,
    string Severity);

public sealed record MyTaskCard(
    string Type,
    string Id,
    string Title,
    string Subtitle,
    string? Meta,
    decimal? Amount,
    DateTime? DateUtc,
    string? Status);

public sealed record MyTasksResult(
    IReadOnlyList<MyTaskSummaryItem> Summary,
    IReadOnlyList<MyTaskCard> Cards);
