using App.Domain.Common;

namespace App.Domain.MaintenanceBilling;

public class MaintenanceCharge : TenantOwnedEntity
{
    public Guid UnitId { get; set; }

    public decimal Amount { get; set; }

    public string Label { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
