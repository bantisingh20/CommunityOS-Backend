using App.Domain.Community;
using App.Domain.Maintenance;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Asset"/> table (Req 35.1-35.4) â€” the maintenance asset register. As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 35.3) and a
/// soft-deleted/archived asset stays in the table but is hidden from ordinary reads (Req 35.4). It
/// is deliberately NOT <c>IUnitScoped</c> â€” an asset is community infrastructure (same stance as
/// <see cref="CustodyLocationEntityConfiguration"/> / ParkingSlot / StorageLocation), even when its
/// optional <c>UnitId</c> ties it to a unit.
///
/// <para><b>Indexed opaque-token lookup (Req 35.2) â€” same pattern as <c>VisitPass</c> /
/// <c>ParcelAuthorization</c>.</b> The unique index on <c>(CommunityId, QrToken)</c> is both the
/// community-uniqueness guarantee for the scannable token and the single indexed read a QR scan
/// resolves through. <c>(CommunityId, SerialNumber)</c> is the DB-enforced serial-number uniqueness
/// within a community (Req 35.1).</para>
///
/// <para>The <c>(CommunityId, Status, AssetType)</c> index backs the register's status/type-filtered
/// list (Req 35.3) as a single indexed read (no N+1). The optional unit FK is <c>Restrict</c> on
/// delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class AssetEntityConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("Assets");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.SerialNumber).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Location).IsRequired().HasMaxLength(300);
        builder.Property(a => a.AssetType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Category).HasMaxLength(100);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);
        builder.Property(a => a.QrToken).IsRequired().HasMaxLength(128);
        builder.Property(a => a.WarrantyProvider).HasMaxLength(200);
        builder.Property(a => a.WarrantyReference).HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        // Optional unit the asset is tied to (Req 35.1). Restrict on delete so a unit referenced by
        // an asset can't be hard-deleted out from under it; deletes are soft deletes anyway.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(a => a.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Opaque-token uniqueness within a community + the QR-scan lookup path (Req 35.2): a scan
        // resolves to exactly one asset via this index.
        builder.HasIndex(a => new { a.CommunityId, a.QrToken })
            .IsUnique()
            .HasDatabaseName("UX_Asset_CommunityId_QrToken");

        // Serial-number uniqueness within a community (Req 35.1): the DB-level source of truth, so a
        // duplicate maps to CONFLICT via UniqueConstraintGuard rather than a read-then-write pre-check.
        builder.HasIndex(a => new { a.CommunityId, a.SerialNumber })
            .IsUnique()
            .HasDatabaseName("UX_Asset_CommunityId_SerialNumber");

        // The register's status/type-filtered list (Req 35.3) as a single indexed read, no N+1.
        builder.HasIndex(a => new { a.CommunityId, a.Status, a.AssetType })
            .HasDatabaseName("IX_Asset_CommunityId_Status_AssetType");
    }
}

/// <summary>
/// Maps the <see cref="AmcContract"/> table (Req 37.2, 37.3). As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and the expiry-reminder stamp /
/// status edits record exactly once under concurrency. NOT <c>IUnitScoped</c> â€” community
/// infrastructure (same stance as <see cref="WorkOrderEntityConfiguration"/>).
///
/// <para><b>Expiry-sweep index (Req 37.3).</b> <c>(CommunityId, Status, EndDateUtc)</c> backs the
/// background AMC-expiry sweep's candidate predicate â€” active contracts ordered by end date â€” as a
/// single indexed read (no table scan, no N+1), the same shape the ticket SLA sweep's
/// <c>(CommunityId, Status, SlaDueAtUtc)</c> index serves. <c>(CommunityId, AssetId)</c> backs an
/// asset's contracts. The asset FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class AmcContractEntityConfiguration : IEntityTypeConfiguration<AmcContract>
{
    public void Configure(EntityTypeBuilder<AmcContract> builder)
    {
        builder.ToTable("AmcContracts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ContractReference).IsRequired().HasMaxLength(200);
        builder.Property(a => a.VendorName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.VendorContact).HasMaxLength(200);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);
        builder.Property(a => a.ContractValue).HasPrecision(18, 2);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        // Link to the covered asset (Req 37.2). Restrict on delete so a referenced asset can't be
        // hard-deleted out from under its contracts; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(a => a.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // AMC-expiry sweep candidate read (Req 37.3): active contracts ordered by end date.
        builder.HasIndex(a => new { a.CommunityId, a.Status, a.EndDateUtc })
            .HasDatabaseName("IX_AmcContract_CommunityId_Status_EndDateUtc");

        // An asset's contracts (Req 37.2).
        builder.HasIndex(a => new { a.CommunityId, a.AssetId })
            .HasDatabaseName("IX_AmcContract_CommunityId_AssetId");
    }
}

/// <summary>
/// Maps the append-only <see cref="EquipmentHistoryEntry"/> register (Req 36.5). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so a history read is tenant-scoped with no
/// bespoke predicate. The <c>AuditSaveChangesInterceptor</c> independently rejects any
/// Modified/Deleted state on this type, making the register append-only at the data layer (Req 36.5)
/// â€” the same guard it applies to <c>AuditLog</c> / <c>CustodyTimelineEntry</c>.
///
/// <para><b>Chronological-read index (Req 36.5).</b> <c>(CommunityId, AssetId, OccurredAtUtc)</c>
/// backs "an asset's equipment history in chronological order" as a single ordered indexed read (no
/// N+1), bounded by the tenant filter.</para>
///
/// <para>The asset / work-order FKs are <c>Restrict</c> on delete so neither can be hard-deleted out
/// from under its history; rows are retained, never physically removed (Req 36.5). No inverse
/// navigation is declared to keep those entities untouched.</para>
/// </summary>
public sealed class EquipmentHistoryEntryEntityConfiguration : IEntityTypeConfiguration<EquipmentHistoryEntry>
{
    public void Configure(EntityTypeBuilder<EquipmentHistoryEntry> builder)
    {
        builder.ToTable("EquipmentHistoryEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Summary).IsRequired().HasMaxLength(400);
        builder.Property(e => e.Detail).HasMaxLength(2000);
        builder.Property(e => e.ActingUserId).HasMaxLength(200);

        // Link to the asset (Req 36.5). Restrict on delete so the history stays traceable (soft
        // delete archives the asset without removing its history).
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(e => e.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional link to the completed work order (Req 36.5). Restrict so a work order isn't
        // hard-deleted out from under the history entry it produced.
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(e => e.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-asset read (Req 36.5).
        builder.HasIndex(e => new { e.CommunityId, e.AssetId, e.OccurredAtUtc })
            .HasDatabaseName("IX_EquipmentHistoryEntry_CommunityId_AssetId_OccurredAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="InspectionChecklist"/> table and its owned <see cref="InspectionChecklistItem"/>
/// child rows (Req 37.1). As <c>TenantOwnedEntity</c>s both inherit the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are
/// tenant-scoped and record exactly once under concurrency. Neither is <c>IUnitScoped</c> â€” community
/// infrastructure (same stance as <see cref="WorkOrderEntityConfiguration"/>).
///
/// <para><b>Indexes (Req 37.1).</b> <c>(CommunityId, AssetId, InspectedAtUtc)</c> backs an asset's
/// inspections in chronological order as a single indexed read; <c>(CommunityId, InspectionChecklistId)</c>
/// backs loading a checklist's items. The asset / work-order FKs are <c>Restrict</c> on delete;
/// deletes are soft deletes anyway. The itemâ†’checklist FK <c>Cascade</c>s so items follow their parent.</para>
/// </summary>
public sealed class InspectionChecklistEntityConfiguration : IEntityTypeConfiguration<InspectionChecklist>
{
    public void Configure(EntityTypeBuilder<InspectionChecklist> builder)
    {
        builder.ToTable("InspectionChecklists");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name).IsRequired().HasMaxLength(200);
        builder.Property(i => i.OverallResult).IsRequired().HasMaxLength(50);
        builder.Property(i => i.InspectedByUserId).HasMaxLength(200);
        builder.Property(i => i.Notes).HasMaxLength(2000);

        // Link to the asset (Req 37.1). Restrict on delete so a referenced asset can't be hard-deleted
        // out from under its inspections; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(i => i.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional link to the work order the inspection was performed under (Req 37.1).
        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(i => i.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Owned items collection (Req 37.1).
        builder.HasMany(i => i.Items)
            .WithOne()
            .HasForeignKey(it => it.InspectionChecklistId)
            .OnDelete(DeleteBehavior.Cascade);

        // Chronological per-asset read (Req 37.1).
        builder.HasIndex(i => new { i.CommunityId, i.AssetId, i.InspectedAtUtc })
            .HasDatabaseName("IX_InspectionChecklist_CommunityId_AssetId_InspectedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="InspectionChecklistItem"/> child rows (Req 37.1). Carries the parent's
/// community; the <c>(CommunityId, InspectionChecklistId)</c> index backs loading a checklist's items
/// in a single read.
/// </summary>
public sealed class InspectionChecklistItemEntityConfiguration : IEntityTypeConfiguration<InspectionChecklistItem>
{
    public void Configure(EntityTypeBuilder<InspectionChecklistItem> builder)
    {
        builder.ToTable("InspectionChecklistItems");
        builder.HasKey(it => it.Id);

        builder.Property(it => it.ItemText).IsRequired().HasMaxLength(400);
        builder.Property(it => it.Result).IsRequired().HasMaxLength(50);
        builder.Property(it => it.Remark).HasMaxLength(1000);

        builder.HasIndex(it => new { it.CommunityId, it.InspectionChecklistId })
            .HasDatabaseName("IX_InspectionChecklistItem_CommunityId_InspectionChecklistId");
    }
}

/// <summary>
/// Maps the <see cref="MaintenancePlan"/> table (Req 36.1) â€” a preventive-maintenance schedule for an
/// asset. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a
/// retired/soft-deleted plan stays traceable but is hidden from ordinary reads. It is deliberately
/// NOT <c>IUnitScoped</c> â€” a plan is community infrastructure (same stance as
/// <see cref="AssetEntityConfiguration"/>).
///
/// <para><b>Due-plan sweep index (Req 36.1).</b> <c>(CommunityId, IsActive, NextDueAtUtc)</c> backs
/// the background sweep's "active plans whose next occurrence is due" predicate as a single ordered
/// indexed read (no table scan, no N+1), so the preventive-generation pass stays fast and bounded at
/// scale. The asset FK is <c>Restrict</c> on delete so an asset can't be hard-deleted out from under
/// its plans; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class MaintenancePlanEntityConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    public void Configure(EntityTypeBuilder<MaintenancePlan> builder)
    {
        builder.ToTable("MaintenancePlans");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.Frequency).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Priority).IsRequired().HasMaxLength(50);

        // Link to the scheduled asset (Req 36.1). Restrict on delete so a referenced asset can't be
        // hard-deleted out from under its plans; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(p => p.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // The preventive-generation sweep's due-plan read (Req 36.1): active plans ordered by their
        // next occurrence, bounded by the tenant filter â€” a single indexed read.
        builder.HasIndex(p => new { p.CommunityId, p.IsActive, p.NextDueAtUtc })
            .HasDatabaseName("IX_MaintenancePlan_CommunityId_IsActive_NextDueAtUtc");

        // The asset's plans list (Req 36.1) as a single indexed read.
        builder.HasIndex(p => new { p.CommunityId, p.AssetId })
            .HasDatabaseName("IX_MaintenancePlan_CommunityId_AssetId");
    }
}

/// <summary>
/// Maps the <see cref="MeterReading"/> table (Req 37.4, 37.5). As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped. NOT <c>IUnitScoped</c> â€” community
/// infrastructure (same stance as <see cref="WorkOrderEntityConfiguration"/>).
///
/// <para><b>Chronological / latest-reading index (Req 37.4, 37.5).</b>
/// <c>(CommunityId, AssetId, MeterType, ReadingAtUtc)</c> backs both the chronological per-asset-meter
/// read AND the "latest recorded reading for this asset + meter" lookup the service uses to enforce
/// the non-decreasing (monotonic) rule â€” a single ordered indexed read (no scan, no N+1). The asset
/// FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class MeterReadingEntityConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> builder)
    {
        builder.ToTable("MeterReadings");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MeterType).IsRequired().HasMaxLength(50);
        builder.Property(m => m.Unit).IsRequired().HasMaxLength(50);
        builder.Property(m => m.Value).HasPrecision(18, 2);
        builder.Property(m => m.RecordedByUserId).HasMaxLength(200);
        builder.Property(m => m.Notes).HasMaxLength(1000);

        // Link to the asset (Req 37.4). Restrict on delete so a referenced asset can't be hard-deleted
        // out from under its readings; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(m => m.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-asset-meter read + latest-reading (monotonic guard) lookup (Req 37.4, 37.5).
        builder.HasIndex(m => new { m.CommunityId, m.AssetId, m.MeterType, m.ReadingAtUtc })
            .HasDatabaseName("IX_MeterReading_CommunityId_AssetId_MeterType_ReadingAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="WorkOrder"/> table (Req 36.1-36.5) â€” preventive/corrective maintenance work
/// orders. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and the
/// assignment / status transitions record exactly once under concurrency. It is deliberately NOT
/// <c>IUnitScoped</c> â€” a work order is community infrastructure (same stance as
/// <see cref="AssetEntityConfiguration"/> / <see cref="MaintenancePlanEntityConfiguration"/>).
///
/// <para><b>List indexes (Req 36.2, 36.3).</b> <c>(CommunityId, Status, Priority)</c> backs the
/// status/priority-filtered work-order list; <c>(CommunityId, AssetId)</c> backs an asset's work
/// orders; <c>(CommunityId, AssignedToUserId)</c> backs "my assigned work orders" â€” each a single
/// indexed read (no N+1). <c>(CommunityId, MaintenancePlanId, ScheduledForUtc)</c> lets the preventive
/// sweep check whether an occurrence already produced a work order. The asset / plan FKs are
/// <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class WorkOrderEntityConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("WorkOrders");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Title).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Description).IsRequired().HasMaxLength(4000);
        builder.Property(w => w.WorkOrderType).IsRequired().HasMaxLength(50);
        builder.Property(w => w.Priority).IsRequired().HasMaxLength(50);
        builder.Property(w => w.Status).IsRequired().HasMaxLength(50);
        builder.Property(w => w.CompletionNotes).HasMaxLength(4000);

        // Link to the asset (Req 36.1, 36.2). Restrict on delete so a referenced asset can't be
        // hard-deleted out from under its work orders; deletes are soft deletes anyway.
        builder.HasOne<Asset>()
            .WithMany()
            .HasForeignKey(w => w.AssetId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional link to the generating plan (Req 36.1). Restrict so a plan isn't hard-deleted out
        // from under the preventive work orders it produced.
        builder.HasOne<MaintenancePlan>()
            .WithMany()
            .HasForeignKey(w => w.MaintenancePlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // Status/priority-filtered list (Req 36.2) as a single indexed read.
        builder.HasIndex(w => new { w.CommunityId, w.Status, w.Priority })
            .HasDatabaseName("IX_WorkOrder_CommunityId_Status_Priority");

        // An asset's work orders (Req 36.2, 36.5).
        builder.HasIndex(w => new { w.CommunityId, w.AssetId })
            .HasDatabaseName("IX_WorkOrder_CommunityId_AssetId");

        // "My assigned work orders" (Req 36.3) â€” staff-facing list.
        builder.HasIndex(w => new { w.CommunityId, w.AssignedToUserId })
            .HasDatabaseName("IX_WorkOrder_CommunityId_AssignedToUserId");

        // Preventive at-most-once check: has this plan already produced a work order for this
        // occurrence? (Req 36.1). Backs the sweep's existence read.
        builder.HasIndex(w => new { w.CommunityId, w.MaintenancePlanId, w.ScheduledForUtc })
            .HasDatabaseName("IX_WorkOrder_CommunityId_MaintenancePlanId_ScheduledForUtc");
    }
}

/// <summary>
/// Maps the <see cref="WorkOrderLabour"/> table (Req 36.4) â€” a labour entry recorded against a work
/// order. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>; NOT <c>IUnitScoped</c> (maintenance
/// cost data is community infrastructure).
///
/// <para><b>Money precision (steering).</b> <see cref="WorkOrderLabour.Hours"/> /
/// <see cref="WorkOrderLabour.HourlyRate"/> / <see cref="WorkOrderLabour.LabourCost"/> are mapped
/// <c>decimal(18,2)</c> so costs are exact, never floating point. The
/// <c>(CommunityId, WorkOrderId)</c> index backs "a work order's labour" as a single indexed read
/// (no N+1). The work-order FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class WorkOrderLabourEntityConfiguration : IEntityTypeConfiguration<WorkOrderLabour>
{
    public void Configure(EntityTypeBuilder<WorkOrderLabour> builder)
    {
        builder.ToTable("WorkOrderLabour");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.WorkerName).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Notes).HasMaxLength(2000);
        builder.Property(l => l.Hours).HasPrecision(18, 2);
        builder.Property(l => l.HourlyRate).HasPrecision(18, 2);
        builder.Property(l => l.LabourCost).HasPrecision(18, 2);

        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(l => l.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.CommunityId, l.WorkOrderId })
            .HasDatabaseName("IX_WorkOrderLabour_CommunityId_WorkOrderId");
    }
}

/// <summary>
/// Maps the <see cref="WorkOrderPart"/> table (Req 36.4) â€” a part/material recorded against a work
/// order. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>; NOT <c>IUnitScoped</c> (maintenance
/// cost data is community infrastructure).
///
/// <para><b>Money precision (steering).</b> <see cref="WorkOrderPart.Quantity"/> /
/// <see cref="WorkOrderPart.UnitCost"/> / <see cref="WorkOrderPart.LineTotal"/> are mapped
/// <c>decimal(18,2)</c> so costs are exact, never floating point. The
/// <c>(CommunityId, WorkOrderId)</c> index backs "a work order's parts" as a single indexed read
/// (no N+1). The work-order FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class WorkOrderPartEntityConfiguration : IEntityTypeConfiguration<WorkOrderPart>
{
    public void Configure(EntityTypeBuilder<WorkOrderPart> builder)
    {
        builder.ToTable("WorkOrderParts");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PartName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.PartNumber).HasMaxLength(100);
        builder.Property(p => p.Quantity).HasPrecision(18, 2);
        builder.Property(p => p.UnitCost).HasPrecision(18, 2);
        builder.Property(p => p.LineTotal).HasPrecision(18, 2);

        builder.HasOne<WorkOrder>()
            .WithMany()
            .HasForeignKey(p => p.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.CommunityId, p.WorkOrderId })
            .HasDatabaseName("IX_WorkOrderPart_CommunityId_WorkOrderId");
    }
}
