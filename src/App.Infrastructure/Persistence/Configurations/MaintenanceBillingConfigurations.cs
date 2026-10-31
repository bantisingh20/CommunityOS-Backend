using App.Domain.Community;
using App.Domain.MaintenanceBilling;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

public sealed class MaintenanceChargeEntityConfiguration : IEntityTypeConfiguration<MaintenanceCharge>
{
    public void Configure(EntityTypeBuilder<MaintenanceCharge> builder)
    {
        builder.ToTable("MaintenanceCharges");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.Label).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(c => c.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.CommunityId, c.UnitId })
            .HasDatabaseName("IX_MaintenanceCharge_CommunityId_UnitId");
    }
}

public sealed class MaintenanceBillEntityConfiguration : IEntityTypeConfiguration<MaintenanceBill>
{
    public void Configure(EntityTypeBuilder<MaintenanceBill> builder)
    {
        builder.ToTable("MaintenanceBills");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Amount).HasPrecision(18, 2);
        builder.Property(b => b.AmountPaid).HasPrecision(18, 2);
        builder.Property(b => b.Label).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(b => b.Status).IsRequired().HasMaxLength(50);

        builder.HasIndex(b => new { b.CommunityId, b.Status, b.PeriodYear, b.PeriodMonth })
            .HasDatabaseName("IX_MaintenanceBill_CommunityId_Status_Period");

        builder.HasIndex(b => new { b.CommunityId, b.UnitId, b.PeriodYear, b.PeriodMonth })
            .HasDatabaseName("IX_MaintenanceBill_CommunityId_UnitId_Period");
    }
}

public sealed class MaintenancePaymentEntityConfiguration : IEntityTypeConfiguration<MaintenancePayment>
{
    public void Configure(EntityTypeBuilder<MaintenancePayment> builder)
    {
        builder.ToTable("MaintenancePayments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Method).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Reference).IsRequired().HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(50).HasDefaultValue("pending_approval");

        builder.HasIndex(p => new { p.CommunityId, p.Status })
            .HasDatabaseName("IX_MaintenancePayment_CommunityId_Status");

        builder.HasOne<MaintenanceBill>()
            .WithMany()
            .HasForeignKey(p => p.MaintenanceBillId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.CommunityId, p.MaintenanceBillId })
            .HasDatabaseName("IX_MaintenancePayment_CommunityId_MaintenanceBillId");
    }
}
