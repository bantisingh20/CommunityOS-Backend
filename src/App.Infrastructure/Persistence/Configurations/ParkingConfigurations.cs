using App.Domain.Community;
using App.Domain.Gate;
using App.Domain.Parking;
using App.Domain.Residents;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="ParkingAssignment"/> register (Req 40.2, 40.3). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so history reads are tenant-scoped (Req 5.2) and
/// a close (EndedAtUtc stamp) is concurrency-checked.
///
/// <para><b>Current-assignment + chronological read index (Req 40.3, 40.5).</b>
/// <c>(CommunityId, ParkingSlotId, StartedAtUtc)</c> backs "a slot's assignment history in
/// chronological order" and the "find the active row for this slot" lookup the reassign/release path
/// runs â€” a single ordered indexed read, no N+1.</para>
///
/// <para><b>One active assignment per slot (Req 40.3 â€” the headline invariant).</b> The service closes
/// the prior active row and inserts the new one in one transaction so a slot never has two active
/// assignments. On SQL Server a filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc IS NULL</c>
/// is added in <c>AppDbContext.OnModelCreating</c> (where the provider is known) as a hard DB
/// guarantee â€” two concurrent assignments of the SAME slot race to the index and exactly one wins; the
/// loser's unique violation maps to CONFLICT via <c>UniqueConstraintGuard</c>. ponytail: the SQLite
/// test fixture has no filtered-index equivalent wired here, so under that provider the invariant rests
/// on the single-transaction close+insert rather than a DB constraint â€” acceptable for the test path;
/// production (SQL Server) gets the index. Same approach as the one-open-owner
/// <see cref="OwnershipHistoryEntityConfiguration"/> and the one-active-custody-location Parcel index.</para>
///
/// <para>Relationships are <c>Restrict</c> on delete so a slot/unit/visitor cannot be hard-deleted out
/// from under its assignment history; rows are retained, never physically removed.</para>
/// </summary>
public sealed class ParkingAssignmentEntityConfiguration : IEntityTypeConfiguration<ParkingAssignment>
{
    public void Configure(EntityTypeBuilder<ParkingAssignment> builder)
    {
        builder.ToTable("ParkingAssignments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AssignmentType).IsRequired().HasMaxLength(100);

        // Link to the slot (Req 40.2). No navigation on the slot side; restrict on delete.
        builder.HasOne<ParkingSlot>()
            .WithMany()
            .HasForeignKey(a => a.ParkingSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional resident-unit subject (Req 40.2). Restrict: keep history traceable.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(a => a.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional visitor subject (Req 40.2, Task 24.2 guest parking). Restrict on delete.
        builder.HasOne<Visitor>()
            .WithMany()
            .HasForeignKey(a => a.VisitorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + active-row lookup per slot (Req 40.3, 40.5).
        builder.HasIndex(a => new { a.CommunityId, a.ParkingSlotId, a.StartedAtUtc })
            .HasDatabaseName("IX_ParkingAssignment_CommunityId_ParkingSlotId_StartedAtUtc");
    }
}

/// <summary>
/// Maps the append-only <see cref="EvReservation"/> register (Req 41.2, 41.3, Task 24.2). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reservation reads are tenant-scoped (Req 5.2)
/// and a close (EndedAtUtc stamp) / status edit is concurrency-checked.
///
/// <para><b>Current-reservation + chronological read index (Req 41.2, 41.3).</b>
/// <c>(CommunityId, ParkingSlotId, ReservedFromUtc)</c> backs "an EV slot's reservation history" and
/// the "find the active reservation for this slot" lookup the release path runs. The
/// <c>(CommunityId, UnitId)</c> index backs the per-unit concurrent-reservation-limit count (Req 41.2)
/// â€” a single indexed count, no scan.</para>
///
/// <para><b>One active reservation per EV slot (Req 41.3 â€” the headline invariant).</b> The service
/// inserts the active reservation row inside one transaction and never leaves two active rows for a
/// slot. On SQL Server a filtered unique index on <c>(ParkingSlotId) WHERE EndedAtUtc IS NULL</c> is
/// added in <c>AppDbContext.OnModelCreating</c> (where the provider is known) as a hard DB guarantee â€”
/// two concurrent reservations of the SAME EV slot race to the index and exactly one wins; the loser's
/// unique violation maps to CONFLICT via <c>UniqueConstraintGuard</c>. ponytail: the SQLite test
/// fixture has no filtered-index equivalent wired here, so under that provider the invariant rests on
/// the single-transaction insert rather than a DB constraint â€” acceptable for the test path; production
/// (SQL Server) gets the index. Same approach as the one-active-assignment
/// <see cref="ParkingAssignmentEntityConfiguration"/>.</para>
///
/// <para>Relationships are <c>Restrict</c> on delete so a slot/unit cannot be hard-deleted out from
/// under its reservation history; the metered <c>EnergyKwh</c> is <c>decimal(18,3)</c> (metered
/// precision), never a float.</para>
/// </summary>
public sealed class EvReservationEntityConfiguration : IEntityTypeConfiguration<EvReservation>
{
    public void Configure(EntityTypeBuilder<EvReservation> builder)
    {
        builder.ToTable("EvReservations");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).IsRequired().HasMaxLength(100);
        builder.Property(r => r.EnergyKwh).HasColumnType("decimal(18,3)");

        // Link to the EV slot (Req 41.2). No navigation on the slot side; restrict on delete.
        builder.HasOne<ParkingSlot>()
            .WithMany()
            .HasForeignKey(r => r.ParkingSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Reserving unit (Req 41.2). Restrict: keep history traceable.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + active-row lookup per slot (Req 41.2, 41.3).
        builder.HasIndex(r => new { r.CommunityId, r.ParkingSlotId, r.ReservedFromUtc })
            .HasDatabaseName("IX_EvReservation_CommunityId_ParkingSlotId_ReservedFromUtc");

        // Per-unit concurrent-reservation-limit count path (Req 41.2).
        builder.HasIndex(r => new { r.CommunityId, r.UnitId })
            .HasDatabaseName("IX_EvReservation_CommunityId_UnitId");
    }
}

/// <summary>
/// Maps the <see cref="ParkingSlot"/> table (Req 19.4, 40.1). As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; its <c>RowVersion</c> guards slot edits.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps slot codes distinct within a community. The optional
/// legacy <c>AssignedUnitId</c> FK (Phase 2, Req 19.4) is <c>Restrict</c> on delete; the
/// <c>(CommunityId, AssignedUnitId)</c> index backs the Phase 2 "a unit's assigned slots" read.</para>
///
/// <para><b>Phase 6 layout (Req 40.1).</b> The optional <c>ZoneId</c> FK links the slot to a
/// <see cref="ParkingZone"/> (<c>Restrict</c> on delete so a zone with slots cannot be hard-deleted
/// out from under them). The <c>(CommunityId, ZoneId, Level)</c> index is the layout/occupancy read
/// path â€” the occupancy dashboard groups active-assignment counts by zone/level over it (no scan).
/// <c>SlotType</c>/<c>Level</c>/<c>SlotNumber</c> are the configurable layout columns. The
/// authoritative "one active assignment per slot" invariant lives on the <c>ParkingAssignment</c>
/// side (its filtered unique index), NOT here â€” the legacy <c>AssignedUnitId</c> is retained for the
/// Phase 2 flow but is no longer the source of truth.</para>
/// </summary>
public sealed class ParkingSlotEntityConfiguration : IEntityTypeConfiguration<ParkingSlot>
{
    public void Configure(EntityTypeBuilder<ParkingSlot> builder)
    {
        builder.ToTable("ParkingSlots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Level).HasMaxLength(100);
        builder.Property(s => s.SlotNumber).HasMaxLength(100);
        builder.Property(s => s.SlotType).IsRequired().HasMaxLength(100);

        // Legacy Phase 2 unit association (Req 19.4). Restrict on delete; retained for backward compat.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(s => s.AssignedUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Phase 6 layout zone (Req 40.1). Restrict: a zone with slots cannot be hard-deleted.
        builder.HasOne<ParkingZone>()
            .WithMany()
            .HasForeignKey(s => s.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CommunityId, s.Code })
            .IsUnique()
            .HasDatabaseName("UX_ParkingSlot_CommunityId_Code");

        builder.HasIndex(s => new { s.CommunityId, s.AssignedUnitId })
            .HasDatabaseName("IX_ParkingSlot_CommunityId_AssignedUnitId");

        // Layout + occupancy read path (Req 40.1, 40.5): slots grouped by zone/level in one indexed
        // read â€” the occupancy dashboard projects active-assignment counts over this.
        builder.HasIndex(s => new { s.CommunityId, s.ZoneId, s.Level })
            .HasDatabaseName("IX_ParkingSlot_CommunityId_ZoneId_Level");
    }
}

/// <summary>
/// Maps the <see cref="ParkingViolation"/> table (Req 40.4). As a <c>TenantOwnedEntity</c> it inherits
/// the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 5.2) and status edits are
/// concurrency-checked.
///
/// <para>The violation always names its <c>ParkingSlotId</c> and may name the offending
/// <c>VehicleId</c> and/or the responsible resident <c>UnitId</c> â€” all <c>Restrict</c> on delete so
/// the referenced rows cannot be hard-deleted out from under a violation record. The
/// <c>EvidenceFileId</c> is a plain opaque File_Service reference (no FK â€” the file lives in the
/// File_Service metadata table and is reached only through its authorized download path), mirroring
/// <c>TicketAttachment.FileId</c>. The <c>(CommunityId, ParkingSlotId, RecordedAtUtc)</c> index is the
/// chronological per-slot violation read path (no scan).</para>
/// </summary>
public sealed class ParkingViolationEntityConfiguration : IEntityTypeConfiguration<ParkingViolation>
{
    public void Configure(EntityTypeBuilder<ParkingViolation> builder)
    {
        builder.ToTable("ParkingViolations");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.ViolationType).IsRequired().HasMaxLength(100);
        builder.Property(v => v.Description).IsRequired().HasMaxLength(2000);
        builder.Property(v => v.Status).IsRequired().HasMaxLength(100);

        // Link to the slot (Req 40.4). Restrict on delete; deletes are soft deletes.
        builder.HasOne<ParkingSlot>()
            .WithMany()
            .HasForeignKey(v => v.ParkingSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional offending vehicle (Req 40.4). Restrict on delete.
        builder.HasOne<Vehicle>()
            .WithMany()
            .HasForeignKey(v => v.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional responsible unit (Req 40.4). Restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(v => v.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-slot violation read path (Req 40.4).
        builder.HasIndex(v => new { v.CommunityId, v.ParkingSlotId, v.RecordedAtUtc })
            .HasDatabaseName("IX_ParkingViolation_CommunityId_ParkingSlotId_RecordedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="ParkingZone"/> table (Req 40.1) â€” a named grouping of parking slots in the
/// layout. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 5.2)
/// and edits are concurrency-checked â€” the same shape as <see cref="CustodyLocationEntityConfiguration"/>.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps zone codes distinct within a community (a duplicate â†’
/// CONFLICT via <c>UniqueConstraintGuard</c>, no read-then-write). <c>ZoneType</c> is the configurable
/// layout classification; <c>Name</c>/<c>Level</c> are the human-facing layout labels.</para>
/// </summary>
public sealed class ParkingZoneEntityConfiguration : IEntityTypeConfiguration<ParkingZone>
{
    public void Configure(EntityTypeBuilder<ParkingZone> builder)
    {
        builder.ToTable("ParkingZones");
        builder.HasKey(z => z.Id);

        builder.Property(z => z.Code).IsRequired().HasMaxLength(100);
        builder.Property(z => z.Name).IsRequired().HasMaxLength(200);
        builder.Property(z => z.ZoneType).IsRequired().HasMaxLength(100);
        builder.Property(z => z.Level).HasMaxLength(100);

        builder.HasIndex(z => new { z.CommunityId, z.Code })
            .IsUnique()
            .HasDatabaseName("UX_ParkingZone_CommunityId_Code");
    }
}
