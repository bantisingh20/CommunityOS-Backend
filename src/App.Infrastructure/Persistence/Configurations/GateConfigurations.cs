using App.Domain.Community;
using App.Domain.Gate;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="RecurringVisitor"/> table (Req 25.2). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident only
/// sees recurring visitors for their own host unit (the ones they created / may revoke).
///
/// <para>The <c>(CommunityId, UnitId, Status)</c> index backs the host unit's active recurring-visitor
/// list and the entry-workflow lookup as a single indexed read (no N+1). The host-unit FK is
/// <c>Restrict</c> on delete; deletes are soft deletes anyway. <c>HostUnitId</c>/<c>IsActive</c>/
/// <c>IsAuthorizedOn</c> are derived members and are not mapped.</para>
/// </summary>
public sealed class RecurringVisitorEntityConfiguration : IEntityTypeConfiguration<RecurringVisitor>
{
    public void Configure(EntityTypeBuilder<RecurringVisitor> builder)
    {
        builder.ToTable("RecurringVisitors");
        builder.HasKey(r => r.Id);

        // HostUnitId is a get/set alias over UnitId; IsActive is derived from Status. Neither is a
        // stored column (IsAuthorizedOn is a method, so EF ignores it automatically).
        builder.Ignore(r => r.HostUnitId);
        builder.Ignore(r => r.IsActive);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Phone).HasMaxLength(50);
        builder.Property(r => r.Category).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(50);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // The host unit's recurring-visitor list + the entry-workflow authorization lookup (Req 25.2).
        builder.HasIndex(r => new { r.CommunityId, r.UnitId, r.Status })
            .HasDatabaseName("IX_RecurringVisitor_CommunityId_UnitId_Status");
    }
}

public sealed class VisitorEntityConfiguration : IEntityTypeConfiguration<Visitor>
{
    public void Configure(EntityTypeBuilder<Visitor> builder)
    {
        builder.ToTable("Visitors");
        builder.HasKey(v => v.Id);

        // HostUnitId is a get/set alias over UnitId; IsAdmittable is derived from Status. Neither is
        // a stored column.
        builder.Ignore(v => v.HostUnitId);
        builder.Ignore(v => v.IsAdmittable);

        builder.Property(v => v.Name).IsRequired().HasMaxLength(200);
        builder.Property(v => v.Phone).HasMaxLength(50);
        builder.Property(v => v.Email).HasMaxLength(256);
        builder.Property(v => v.QrToken).HasMaxLength(128);
        builder.Property(v => v.OtpHash).HasMaxLength(256);
        builder.HasIndex(v => new { v.CommunityId, v.QrToken })
            .IsUnique()
            .HasFilter("[QrToken] IS NOT NULL")
            .HasDatabaseName("UX_Visitor_CommunityId_QrToken");
        builder.Property(v => v.Category).IsRequired().HasMaxLength(100);
        builder.Property(v => v.VehicleNumber).HasMaxLength(50);
        builder.Property(v => v.GateLocation).HasMaxLength(200);
        builder.Property(v => v.IdProofType).HasMaxLength(100);
        builder.Property(v => v.IdProofNumber).HasMaxLength(100);
        builder.Property(v => v.Purpose).HasMaxLength(500);
        builder.Property(v => v.WhomToMeet).HasMaxLength(200);
        builder.Property(v => v.Status).IsRequired().HasMaxLength(50);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(v => v.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // The host unit's pending-walk-in approval queue + the resident self-view list (Req 24.5).
        builder.HasIndex(v => new { v.CommunityId, v.UnitId, v.Status })
            .HasDatabaseName("IX_Visitor_CommunityId_UnitId_Status");
    }
}

public sealed class VisitPassEntityConfiguration : IEntityTypeConfiguration<VisitPass>
{
    public void Configure(EntityTypeBuilder<VisitPass> builder)
    {
        builder.ToTable("VisitPasses");
        builder.HasKey(p => p.Id);

        // HostUnitId is a get/set alias over UnitId (the IUnitScoped backing column) â€” ignore it so
        // EF maps the single UnitId column, not a duplicate.
        builder.Ignore(p => p.HostUnitId);

        builder.Property(p => p.VisitorName).IsRequired().HasMaxLength(200);
        builder.Property(p => p.QrToken).IsRequired().HasMaxLength(128);
        builder.Property(p => p.PublicToken).IsRequired().HasMaxLength(128).HasDefaultValue(string.Empty);
        builder.Property(p => p.OtpHash).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Phone).HasMaxLength(50);
        builder.Property(p => p.Email).HasMaxLength(256);
        builder.Property(p => p.VehicleNumber).HasMaxLength(50);
        builder.Property(p => p.VehicleType).HasMaxLength(100);
        builder.Property(p => p.WhomToMeet).HasMaxLength(200);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);


        builder.HasIndex(p => new { p.CommunityId, p.QrToken })
            .IsUnique()
            .HasDatabaseName("UX_VisitPass_CommunityId_QrToken");

        // Globally-unique public link token (the public lookup has no tenant context). Filtered so
        // legacy rows with an empty token don't collide on the unique index.
        builder.HasIndex(p => p.PublicToken)
            .IsUnique()
            .HasFilter("[PublicToken] <> ''")
            .HasDatabaseName("UX_VisitPass_PublicToken");

        // "A unit's passes" for the resident self-view list (Req 23.5).
        builder.HasIndex(p => new { p.CommunityId, p.UnitId })
            .HasDatabaseName("IX_VisitPass_CommunityId_UnitId");
    }
}

/// <summary>
/// Maps the <see cref="AccessIntegrationEvent"/> table (Req 27.5). As a <c>TenantOwnedEntity</c>
/// (NOT <c>IUnitScoped</c> â€” a gate operations record) it inherits the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para>An append-only record of each triggered CCTV / boom-barrier / access-control hook; the real
/// hardware drivers are Phase 12 (Task 40). The <c>(CommunityId, CreatedAtUtc)</c> index backs the
/// chronological integration-event log as a single indexed read (Task 16.4).</para>
/// </summary>
public sealed class AccessIntegrationEventEntityConfiguration : IEntityTypeConfiguration<AccessIntegrationEvent>
{
    public void Configure(EntityTypeBuilder<AccessIntegrationEvent> builder)
    {
        builder.ToTable("AccessIntegrationEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.GateLabel).HasMaxLength(200);
        builder.Property(e => e.Action).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Detail).HasMaxLength(1000);

        builder.HasIndex(e => new { e.CommunityId, e.CreatedAtUtc })
            .HasDatabaseName("IX_AccessIntegrationEvent_CommunityId_CreatedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="EntryExitEvent"/> table (Req 25.3, 25.4). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> â€” a gate operations log, not resident self-view data) it inherits the tenant +
/// soft-delete global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>;
/// the rowversion makes the exit stamp concurrency-safe so a double-exit can't both win (Req 25.3).
///
/// <para><b>Scalable chronological log + open-event matching (Req 25.4).</b> The
/// <c>(CommunityId, EntryTsUtc)</c> index serves both the default newest-first entry/exit list (16.4)
/// and the exit-matching read ("the open event for this authorization") as a single indexed query.
/// The authorization-link indexes make matching the open event by VisitorId/VisitPassId/
/// RecurringVisitorId a direct indexed lookup rather than a scan.</para>
///
/// <para>The authorization-reference FKs are intentionally NOT modeled as navigations â€” the event is
/// a standalone, self-describing log row (denormalized VisitorName/Category) that must survive if the
/// source authorization is later edited/soft-deleted, so it only stores the id.</para>
/// </summary>
public sealed class EntryExitEventEntityConfiguration : IEntityTypeConfiguration<EntryExitEvent>
{
    public void Configure(EntityTypeBuilder<EntryExitEvent> builder)
    {
        builder.ToTable("EntryExitEvents");
        builder.HasKey(e => e.Id);

        // IsOpen is derived from ExitTsUtc; not a stored column.
        builder.Ignore(e => e.IsOpen);

        builder.Property(e => e.GateLabel).HasMaxLength(200);
        builder.Property(e => e.VisitorName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Category).IsRequired().HasMaxLength(100);
        builder.Property(e => e.VehicleNumber).HasMaxLength(50);

        // Scalable chronological entry/exit log (Req 25.4) + the exit-matching read path (Req 25.3).
        builder.HasIndex(e => new { e.CommunityId, e.EntryTsUtc })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_EntryTsUtc");

        // Direct indexed lookup of the OPEN event for a given authorization when recording an exit
        // (Req 25.3 â€” "exit matched to the open event"), so matching is a single indexed query.
        builder.HasIndex(e => new { e.CommunityId, e.VisitorId })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_VisitorId");
        builder.HasIndex(e => new { e.CommunityId, e.VisitPassId })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_VisitPassId");
        builder.HasIndex(e => new { e.CommunityId, e.RecurringVisitorId })
            .HasDatabaseName("IX_EntryExitEvent_CommunityId_RecurringVisitorId");
    }
}

/// <summary>
/// Maps the <see cref="GuardShift"/> table (Req 26.4). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> â€” a community-wide roster) it inherits the tenant + soft-delete global filter
/// and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para>The <c>(CommunityId, ShiftStartUtc)</c> index backs the chronological roster list (Task
/// 16.4) as a single indexed read; <c>(CommunityId, GuardUserId)</c> backs "a guard's shifts". The
/// shift references the guard <c>User</c> by id only (a Phase 1 Guard-role user) â€” not a duplicated
/// profile and not a hard FK navigation, since <c>User</c> is a platform-scoped entity in another
/// module (Req 26.2).</para>
/// </summary>
public sealed class GuardShiftEntityConfiguration : IEntityTypeConfiguration<GuardShift>
{
    public void Configure(EntityTypeBuilder<GuardShift> builder)
    {
        builder.ToTable("GuardShifts");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.GateLabel).HasMaxLength(200);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(50);

        // Chronological roster list (Task 16.4) + "a guard's shifts" read, both single indexed reads.
        builder.HasIndex(s => new { s.CommunityId, s.ShiftStartUtc })
            .HasDatabaseName("IX_GuardShift_CommunityId_ShiftStartUtc");
        builder.HasIndex(s => new { s.CommunityId, s.GuardUserId })
            .HasDatabaseName("IX_GuardShift_CommunityId_GuardUserId");
    }
}

/// <summary>
/// Maps the <see cref="Incident"/> table (Req 27.1, 27.2). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> â€” a community-wide security record) it inherits the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>; the rowversion makes
/// the acknowledge/resolve transitions record exactly once under concurrency (Req 27.1).
///
/// <para>The <c>(CommunityId, Status)</c> index backs the open-incidents queue + the status-filtered
/// list (Task 16.4); <c>(CommunityId, CreatedAtUtc)</c> backs the chronological list. <c>IsResolved</c>
/// / <c>IsEscalated</c> are derived from Status/EscalationState and are not mapped.</para>
/// </summary>
public sealed class IncidentEntityConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("Incidents");
        builder.HasKey(i => i.Id);

        builder.Ignore(i => i.IsResolved);
        builder.Ignore(i => i.IsEscalated);

        builder.Property(i => i.Location).HasMaxLength(200);
        builder.Property(i => i.Category).IsRequired().HasMaxLength(100);
        builder.Property(i => i.Severity).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Description).IsRequired().HasMaxLength(2000);
        builder.Property(i => i.Status).IsRequired().HasMaxLength(50);
        builder.Property(i => i.EscalationState).IsRequired().HasMaxLength(50);

        builder.HasIndex(i => new { i.CommunityId, i.Status })
            .HasDatabaseName("IX_Incident_CommunityId_Status");
        builder.HasIndex(i => new { i.CommunityId, i.CreatedAtUtc })
            .HasDatabaseName("IX_Incident_CommunityId_CreatedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="SosAlert"/> table (Req 27.3, 27.4). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> â€” a community-wide emergency event handled by security staff; its
/// <c>UnitId</c> is a plain reference, not the <c>IUnitScoped</c> contract) it inherits the tenant +
/// soft-delete global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>;
/// the rowversion makes the acknowledge/resolve transition record exactly once (Req 27.4).
///
/// <para>The <c>(CommunityId, Status)</c> index backs the active-SOS queue (Req 27.4) + the
/// status-filtered list (Task 16.4); <c>(CommunityId, RaisedAtUtc)</c> backs the chronological list.
/// <c>IsActive</c> is derived from Status and is not mapped.</para>
/// </summary>
public sealed class SosAlertEntityConfiguration : IEntityTypeConfiguration<SosAlert>
{
    public void Configure(EntityTypeBuilder<SosAlert> builder)
    {
        builder.ToTable("SosAlerts");
        builder.HasKey(a => a.Id);

        builder.Ignore(a => a.IsActive);

        builder.Property(a => a.Location).HasMaxLength(200);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);

        builder.HasIndex(a => new { a.CommunityId, a.Status })
            .HasDatabaseName("IX_SosAlert_CommunityId_Status");
        builder.HasIndex(a => new { a.CommunityId, a.RaisedAtUtc })
            .HasDatabaseName("IX_SosAlert_CommunityId_RaisedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="WatchlistEntry"/> table (Req 26.1, 26.5). As a <c>TenantOwnedEntity</c> (NOT
/// <c>IUnitScoped</c> â€” a community-wide security register, not resident self-view data) it inherits
/// the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; the rowversion makes the clear transition record exactly once.
///
/// <para><b>Efficient gate match (Req 26.5).</b> The <c>(CommunityId, SubjectIdentifier)</c> index is
/// the single indexed equality lookup the gate flow runs on every presented subject â€” no scan, no
/// N+1. The service stores and matches the identifier normalized (trim + upper-invariant) so casing/
/// spacing can't slip a match. <c>IsActive</c> is derived from Status and is not mapped.</para>
/// </summary>
public sealed class WatchlistEntryEntityConfiguration : IEntityTypeConfiguration<WatchlistEntry>
{
    public void Configure(EntityTypeBuilder<WatchlistEntry> builder)
    {
        builder.ToTable("WatchlistEntries");
        builder.HasKey(e => e.Id);

        builder.Ignore(e => e.IsActive);

        builder.Property(e => e.SubjectType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.SubjectIdentifier).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Reason).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Severity).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(50);

        // The gate-match lookup path (Req 26.5): a presented subject resolves via this index.
        builder.HasIndex(e => new { e.CommunityId, e.SubjectIdentifier })
            .HasDatabaseName("IX_WatchlistEntry_CommunityId_SubjectIdentifier");
    }
}
