using App.Domain.Community;
using App.Domain.Parcel;
using DomainParcel = App.Domain.Parcel.Parcel;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Parcel"/> table (Req 28.1â€“28.4, 29.3). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident only
/// sees parcels addressed to their own recipient unit, and the rowversion makes the status transition
/// record exactly once under concurrency (Req 28.3, design "RowVersion on Parcel status").
///
/// <para>The <c>(CommunityId, UnitId, Status)</c> index backs the recipient unit's parcel list +
/// the resident self-view as a single indexed read (no N+1); <c>(CommunityId, Status)</c> backs the
/// gate's status-filtered queue (e.g. in-custody / ready-for-pickup). The recipient-unit FK is
/// <c>Restrict</c> on delete; deletes are soft deletes anyway. <c>RecipientUnitId</c> is a get/set
/// alias over <c>UnitId</c> and is not a stored column.</para>
/// </summary>
public sealed class ParcelEntityConfiguration : IEntityTypeConfiguration<Domain.Parcel.Parcel>
{
    public void Configure(EntityTypeBuilder<Domain.Parcel.Parcel> builder)
    {
        builder.ToTable("Parcels");
        builder.HasKey(p => p.Id);

        // RecipientUnitId is a get/set alias over UnitId; not a stored column.
        builder.Ignore(p => p.RecipientUnitId);

        builder.Property(p => p.TrackingNumber).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Provider).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Category).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Status).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Condition).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Receiver).HasMaxLength(200);
        builder.Property(p => p.ReceivedLocation).HasMaxLength(200);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Active custody location (Task 18.2, Req 29.1). Optional FK, Restrict on delete so a custody
        // location in use can't be hard-deleted out from under a held parcel. The "one active custody
        // location â†’ one parcel" uniqueness (Req 29.4) is the filtered unique index on
        // (CustodyLocationId) WHERE CustodyLocationId IS NOT NULL added in AppDbContext.OnModelCreating.
        builder.HasOne<CustodyLocation>()
            .WithMany()
            .HasForeignKey(p => p.CustodyLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        // The recipient unit's parcel list + the resident self-view list (Req 28.5, 5.4).
        builder.HasIndex(p => new { p.CommunityId, p.UnitId, p.Status })
            .HasDatabaseName("IX_Parcel_CommunityId_UnitId_Status");

        // The gate's status-filtered queue (received / in-custody / ready-for-pickup), Req 28.5.
        builder.HasIndex(p => new { p.CommunityId, p.Status })
            .HasDatabaseName("IX_Parcel_CommunityId_Status");
    }
}

/// <summary>
/// Maps the <see cref="CustodyLocation"/> table (Req 29.1) â€” the secure bin/location a parcel is held
/// in while in custody. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global
/// filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are
/// tenant-scoped (Req 5.2) â€” the same shape as <see cref="StorageLocationEntityConfiguration"/>.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps location codes distinct within a community. The
/// "one active custody location holds at most one parcel" invariant (Req 29.4) is NOT modelled here
/// â€” it lives on the <c>Parcel.CustodyLocationId</c> side as a filtered unique index added in
/// <c>AppDbContext.OnModelCreating</c> (where the provider is known), mirroring the one-open-owner
/// filtered index on <see cref="OwnershipHistoryEntityConfiguration"/>.</para>
/// </summary>
public sealed class CustodyLocationEntityConfiguration : IEntityTypeConfiguration<CustodyLocation>
{
    public void Configure(EntityTypeBuilder<CustodyLocation> builder)
    {
        builder.ToTable("CustodyLocations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Code).IsRequired().HasMaxLength(100);

        builder.HasIndex(c => new { c.CommunityId, c.Code })
            .IsUnique()
            .HasDatabaseName("UX_CustodyLocation_CommunityId_Code");
    }
}

/// <summary>
/// Maps the append-only <see cref="CustodyTimelineEntry"/> register (Req 31.1, 31.5). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so a timeline read is tenant-scoped (Req 5.2,
/// 31.5) with no bespoke predicate. The <c>AuditSaveChangesInterceptor</c> independently rejects any
/// Modified/Deleted state on this type, making the register append-only at the data layer (Req 31.1)
/// â€” the same guard it applies to <c>AuditLog</c>.
///
/// <para><b>Chronological-read index (Req 31.5).</b> <c>(CommunityId, ParcelId, OccurredAtUtc)</c>
/// backs "a parcel's custody timeline in chronological order" as a single ordered indexed read (no
/// N+1), bounded by the tenant filter.</para>
///
/// <para>The parcel FK is <c>Restrict</c> on delete so a parcel cannot be hard-deleted out from under
/// its timeline; rows are retained, never physically removed (Req 31.1). No inverse navigation is
/// declared on <see cref="DomainParcel"/> to keep that entity untouched.</para>
/// </summary>
public sealed class CustodyTimelineEntryEntityConfiguration : IEntityTypeConfiguration<CustodyTimelineEntry>
{
    public void Configure(EntityTypeBuilder<CustodyTimelineEntry> builder)
    {
        builder.ToTable("CustodyTimelineEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(50);
        builder.Property(e => e.FromStatus).HasMaxLength(50);
        builder.Property(e => e.ToStatus).HasMaxLength(50);
        builder.Property(e => e.ActingUserId).HasMaxLength(200);
        builder.Property(e => e.Detail).HasMaxLength(2000);

        // Link to the parcel (Req 31.1). No navigation on the Parcel side; restrict on delete so the
        // timeline stays traceable (soft delete archives the parcel without removing its history).
        builder.HasOne<DomainParcel>()
            .WithMany()
            .HasForeignKey(e => e.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological per-parcel read (Req 31.5).
        builder.HasIndex(e => new { e.CommunityId, e.ParcelId, e.OccurredAtUtc })
            .HasDatabaseName("IX_CustodyTimelineEntry_CommunityId_ParcelId_OccurredAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="ParcelAuthorization"/> table (Req 30.1, 30.2). As a <c>TenantOwnedEntity</c>
/// that is also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global
/// filter (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident
/// principal only sees authorizations for their own unit, and the rowversion makes consuming an
/// authorization on handover record exactly once under concurrency (Req 30.4).
///
/// <para><b>Indexed opaque-token lookup (Req 30.2) â€” same pattern as <c>VisitPass</c>.</b> The unique
/// index on <c>(CommunityId, QrToken)</c> is both the uniqueness guarantee for the opaque QR token
/// within a community and the single indexed read the guard's QR handover rides. The
/// <c>(CommunityId, ParcelId)</c> index backs "an authorization for this parcel" lookup at handover.</para>
///
/// <para>The parcel FK is <c>Restrict</c> on delete (deletes are soft deletes anyway);
/// <c>RecipientUnitId</c>-style <c>UnitId</c> is the <c>IUnitScoped</c> backing column.</para>
/// </summary>
public sealed class ParcelAuthorizationEntityConfiguration : IEntityTypeConfiguration<ParcelAuthorization>
{
    public void Configure(EntityTypeBuilder<ParcelAuthorization> builder)
    {
        builder.ToTable("ParcelAuthorizations");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.CollectorName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.CollectorRelationship).HasMaxLength(200);
        builder.Property(a => a.ExpectedIdReference).HasMaxLength(200);
        builder.Property(a => a.QrToken).IsRequired().HasMaxLength(128);
        builder.Property(a => a.OtpHash).IsRequired().HasMaxLength(256);
        builder.Property(a => a.VerificationMethod).IsRequired().HasMaxLength(50);

        builder.HasOne<Domain.Parcel.Parcel>()
            .WithMany()
            .HasForeignKey(a => a.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        // Opaque-token uniqueness within a community + the guard QR-handover lookup path (Req 30.2):
        // a QR scan resolves to exactly one authorization via this index.
        builder.HasIndex(a => new { a.CommunityId, a.QrToken })
            .IsUnique()
            .HasDatabaseName("UX_ParcelAuthorization_CommunityId_QrToken");

        // "The authorizations for this parcel" lookup at handover (Req 30.2) and the resident
        // self-view list.
        builder.HasIndex(a => new { a.CommunityId, a.ParcelId })
            .HasDatabaseName("IX_ParcelAuthorization_CommunityId_ParcelId");
    }
}

/// <summary>
/// Maps the <see cref="ParcelHandover"/> table (Req 30.4). As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident
/// principal only sees handovers for their own unit's parcels.
///
/// <para><b>One handover per parcel (Req 30.4).</b> The unique index on <c>(ParcelId)</c> makes
/// "a parcel is handed over at most once" a hard DB guarantee: even if the idempotency-key wrapper
/// were bypassed, a second handover insert for the same parcel trips this unique constraint. Combined
/// with the <c>Idempotency-Key</c> at-most-once wrapper on the operation this gives belt-and-braces
/// exactly-once handover (Req 30.4, 30.5).</para>
///
/// <para>The parcel + authorization FKs are <c>Restrict</c> on delete (deletes are soft deletes
/// anyway). <c>ProofFileId</c> is the opaque File_Service reference to the captured handover proof.</para>
/// </summary>
public sealed class ParcelHandoverEntityConfiguration : IEntityTypeConfiguration<ParcelHandover>
{
    public void Configure(EntityTypeBuilder<ParcelHandover> builder)
    {
        builder.ToTable("ParcelHandovers");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.CollectorName).IsRequired().HasMaxLength(200);
        builder.Property(h => h.CollectorRelationship).HasMaxLength(200);
        builder.Property(h => h.VerificationMethod).IsRequired().HasMaxLength(50);
        builder.Property(h => h.GuardUserId).HasMaxLength(128);

        builder.HasOne<Domain.Parcel.Parcel>()
            .WithMany()
            .HasForeignKey(h => h.ParcelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ParcelAuthorization>()
            .WithMany()
            .HasForeignKey(h => h.ParcelAuthorizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // One handover per parcel (Req 30.4): the DB guarantee behind at-most-once handover,
        // alongside the Idempotency-Key wrapper (Req 30.5).
        builder.HasIndex(h => h.ParcelId)
            .IsUnique()
            .HasDatabaseName("UX_ParcelHandover_ParcelId");
    }
}
