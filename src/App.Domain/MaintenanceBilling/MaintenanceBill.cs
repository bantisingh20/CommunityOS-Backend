using App.Domain.Common;

namespace App.Domain.MaintenanceBilling;

public class MaintenanceBill : TenantOwnedEntity, IUnitScoped
{
    public Guid? UnitId { get; set; }

    public int PeriodYear { get; set; }

    public int PeriodMonth { get; set; }

    public decimal Amount { get; set; }

    public decimal AmountPaid { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Status { get; set; } = MaintenanceBillStatuses.Unpaid;

    public DateTime? DueDateUtc { get; set; }

    public DateTime IssuedAtUtc { get; set; }
}

public static class MaintenanceBillStatuses
{
    public const string Unpaid = "unpaid";

    public const string PartiallyPaid = "partially_paid";

    public const string Paid = "paid";

    public const string Waived = "waived";
}
