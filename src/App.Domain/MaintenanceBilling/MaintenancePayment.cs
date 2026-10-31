using App.Domain.Common;

namespace App.Domain.MaintenanceBilling;

public class MaintenancePayment : TenantOwnedEntity, IUnitScoped
{
    public Guid MaintenanceBillId { get; set; }

    public Guid? UnitId { get; set; }

    public decimal Amount { get; set; }

    public string Method { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public DateTime PaidAtUtc { get; set; }

    public string Status { get; set; } = MaintenancePaymentStatuses.PendingApproval;

    public Guid? ApprovedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    public bool SubmittedByResident { get; set; }
}

public static class MaintenancePaymentStatuses
{
    public const string PendingApproval = "pending_approval";

    public const string Approved = "approved";

    public const string Rejected = "rejected";
}

public static class MaintenancePaymentMethods
{
    public const string Cash = "cash";

    public const string Cheque = "cheque";

    public const string Upi = "upi";

    public const string BankTransfer = "bank_transfer";

    public const string Online = "online";
}
