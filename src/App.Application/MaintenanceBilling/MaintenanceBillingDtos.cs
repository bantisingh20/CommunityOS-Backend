namespace App.Application.MaintenanceBilling;

public sealed record MaintenanceChargeDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    decimal Amount,
    string Label,
    bool IsActive);

public sealed record SetMaintenanceChargeRequest(
    Guid CommunityId,
    Guid UnitId,
    decimal Amount,
    string? Label,
    bool IsActive);

public sealed record BulkSetMaintenanceChargeRequest(
    Guid CommunityId,
    IReadOnlyList<Guid> UnitIds,
    decimal Amount,
    string? Label,
    bool IsActive,
    bool ApplyToAllUnits);

public sealed record BulkSetMaintenanceChargeResult(
    Guid CommunityId,
    int Created,
    int Updated);

public sealed record MaintenanceBillDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    int PeriodYear,
    int PeriodMonth,
    decimal Amount,
    decimal AmountPaid,
    decimal AmountDue,
    string Label,
    string Status,
    DateTime? DueDateUtc,
    DateTime IssuedAtUtc);

public sealed record GenerateBillsRequest(
    Guid CommunityId,
    int PeriodYear,
    int PeriodMonth,
    DateTime? DueDateUtc);

public sealed record GenerateBillsResult(
    Guid CommunityId,
    int PeriodYear,
    int PeriodMonth,
    int BillsCreated,
    int UnitsSkipped);

public sealed record MaintenancePaymentDto(
    Guid Id,
    Guid CommunityId,
    Guid MaintenanceBillId,
    Guid UnitId,
    decimal Amount,
    string Method,
    string Reference,
    DateTime PaidAtUtc,
    string Status,
    bool SubmittedByResident,
    DateTime? ApprovedAtUtc);

public sealed record RecordMaintenancePaymentRequest(
    Guid MaintenanceBillId,
    decimal Amount,
    string Method,
    string? Reference,
    DateTime? PaidAtUtc,
    bool SubmittedByResident = false,
    bool AutoApprove = true);

public sealed record PendingPaymentDto(
    Guid Id,
    Guid CommunityId,
    Guid MaintenanceBillId,
    Guid UnitId,
    int PeriodYear,
    int PeriodMonth,
    decimal Amount,
    string Method,
    string Reference,
    DateTime PaidAtUtc,
    bool SubmittedByResident);
