using App.Domain.Community;
using App.Domain.Residents;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="HouseholdMember"/> link table (Req 15.2) that associates a
/// <see cref="Resident"/> with a Unit. As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + <b>resident self-scope</b> global
/// filter (Req 5.4, 66.3) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a
/// resident-self-scoped principal therefore only sees their own household rows.
///
/// <para>Relationships are <c>Restrict</c> on delete so a resident or unit cannot be hard-deleted
/// out from under its household rows; deletes are soft deletes anyway. The
/// <c>(CommunityId, ResidentId)</c> index backs "a resident's household associations" and the
/// <c>(CommunityId, UnitId)</c> index backs "a unit's current/past household" â€” both as single
/// indexed reads (no N+1).</para>
/// </summary>
public sealed class HouseholdMemberEntityConfiguration : IEntityTypeConfiguration<HouseholdMember>
{
    public void Configure(EntityTypeBuilder<HouseholdMember> builder)
    {
        builder.ToTable("HouseholdMembers");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Relationship).IsRequired().HasMaxLength(100);

        // Resident â†’ household members (one-to-many). Restrict: keep the link traceable; soft delete
        // handles archival.
        builder.HasOne(h => h.Resident)
            .WithMany(r => r.HouseholdMembers)
            .HasForeignKey(h => h.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Link to the unit (Req 15.2). No navigation on Unit's side; restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(h => h.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Read paths: by resident (a resident's associations) and by unit (a unit's household).
        builder.HasIndex(h => new { h.CommunityId, h.ResidentId })
            .HasDatabaseName("IX_HouseholdMember_CommunityId_ResidentId");
        builder.HasIndex(h => new { h.CommunityId, h.UnitId })
            .HasDatabaseName("IX_HouseholdMember_CommunityId_UnitId");
    }
}

/// <summary>
/// Maps the append-only <see cref="OwnershipHistory"/> register (Req 16.1, 16.3). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so history reads are tenant-scoped (Req 5.2)
/// and a close (EndDate stamp) is concurrency-checked.
///
/// <para><b>Chronological-read index (Req 16.4).</b> <c>(CommunityId, UnitId, StartDate)</c> backs
/// "a unit's ownership history in chronological order" as a single ordered indexed read (no N+1),
/// and also the "find the open row for this unit" lookup the transfer path runs.</para>
///
/// <para><b>At-most-one-open-owner (Req 16.3 invariant).</b> The service closes the prior open row
/// and inserts the new owner in one transaction so a unit never has two open owners. On SQL Server
/// a filtered unique index on <c>(UnitId) WHERE EndDate IS NULL</c> is added in
/// <c>AppDbContext.OnModelCreating</c> (where the provider is known) as a hard DB guarantee; the
/// SQLite test path relies on the transactional close+insert. ponytail: the SQLite fixture has no
/// filtered-index equivalent wired here, so under that provider the invariant rests on the
/// single-transaction close+insert rather than a DB constraint â€” acceptable for the test path;
/// production (SQL Server) gets the index.</para>
///
/// <para>Relationships are <c>Restrict</c> on delete so a resident or unit cannot be hard-deleted out
/// from under its history; history rows are retained, never physically removed (Req 16.3). No inverse
/// navigation is declared on <see cref="Resident"/> to keep that entity untouched.</para>
/// </summary>
public sealed class OwnershipHistoryEntityConfiguration : IEntityTypeConfiguration<OwnershipHistory>
{
    public void Configure(EntityTypeBuilder<OwnershipHistory> builder)
    {
        builder.ToTable("OwnershipHistories");
        builder.HasKey(o => o.Id);

        // Owner (resident) â†’ ownership rows. Restrict: keep history traceable; soft delete archives.
        builder.HasOne(o => o.Resident)
            .WithMany()
            .HasForeignKey(o => o.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Link to the unit (Req 16.1). No navigation on Unit's side; restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(o => o.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + open-row lookup per unit (Req 16.4).
        builder.HasIndex(o => new { o.CommunityId, o.UnitId, o.StartDate })
            .HasDatabaseName("IX_OwnershipHistory_CommunityId_UnitId_StartDate");
    }
}

/// <summary>
/// Maps the <see cref="Pet"/> table (Req 19.3). As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4, 19.5) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident
/// principal only sees their own unit's pets.
///
/// <para>No uniqueness constraint â€” a unit may register several pets (Req 19.3 stores type + owning
/// unit only). The <c>(CommunityId, UnitId)</c> index backs "a unit's pets" as a single indexed read
/// (no N+1). The unit FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class PetEntityConfiguration : IEntityTypeConfiguration<Pet>
{
    public void Configure(EntityTypeBuilder<Pet> builder)
    {
        builder.ToTable("Pets");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PetType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Name).HasMaxLength(200);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.CommunityId, p.UnitId })
            .HasDatabaseName("IX_Pet_CommunityId_UnitId");
    }
}

/// <summary>
/// Maps the <see cref="ResidentJoinRequest"/> table (resident self-onboarding). A
/// <c>TenantOwnedEntity</c>, so it inherits the tenant + soft-delete global filter and rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>. NOT <c>IUnitScoped</c> â€” a join request is read
/// by community ADMINS (not residents), so it's community-scoped only.
///
/// <para>The <c>(CommunityId, Status, CreatedAtUtc)</c> index backs the admin "pending requests"
/// list as a single indexed read. The <c>(CommunityId, UnitId)</c> index backs the one-active-
/// request-per-unit check at submit. The unit FK is <c>Restrict</c> on delete.</para>
/// </summary>
public sealed class ResidentJoinRequestEntityConfiguration : IEntityTypeConfiguration<ResidentJoinRequest>
{
    public void Configure(EntityTypeBuilder<ResidentJoinRequest> builder)
    {
        builder.ToTable("ResidentJoinRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(256);
        builder.Property(r => r.Phone).IsRequired().HasMaxLength(32);
        builder.Property(r => r.PinHash).IsRequired().HasMaxLength(512);
        builder.Property(r => r.Status).IsRequired().HasMaxLength(32);
        builder.Property(r => r.DecidedBy).HasMaxLength(200);
        builder.Property(r => r.RejectReason).HasMaxLength(500);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(r => r.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.CommunityId, r.Status, r.CreatedAtUtc })
            .HasDatabaseName("IX_ResidentJoinRequest_CommunityId_Status_CreatedAtUtc");

        builder.HasIndex(r => new { r.CommunityId, r.UnitId })
            .HasDatabaseName("IX_ResidentJoinRequest_CommunityId_UnitId");
    }
}

/// <summary>
/// Maps the <see cref="StorageLocation"/> table (Req 19.4) â€” the storage counterpart of
/// <see cref="ParkingSlot"/>. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete
/// global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para>Unique <c>(CommunityId, Code)</c> keeps location codes distinct within a community; the
/// optional <c>AssignedUnitId</c> FK is <c>Restrict</c> on delete and the <c>(CommunityId,
/// AssignedUnitId)</c> index backs "a unit's assigned storage" as a single indexed read (Req 19.5).
/// ponytail: single reassignable owner, no active-assignment uniqueness â€” Phase 6 scope.</para>
/// </summary>
public sealed class StorageLocationEntityConfiguration : IEntityTypeConfiguration<StorageLocation>
{
    public void Configure(EntityTypeBuilder<StorageLocation> builder)
    {
        builder.ToTable("StorageLocations");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Code).IsRequired().HasMaxLength(100);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(s => s.AssignedUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.CommunityId, s.Code })
            .IsUnique()
            .HasDatabaseName("UX_StorageLocation_CommunityId_Code");

        builder.HasIndex(s => new { s.CommunityId, s.AssignedUnitId })
            .HasDatabaseName("IX_StorageLocation_CommunityId_AssignedUnitId");
    }
}

/// <summary>
/// Maps the append-only <see cref="TenancyHistory"/> register (Req 16.2, 16.3). As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so history reads are tenant-scoped (Req 5.2)
/// and ending a tenancy (EndDate stamp) is concurrency-checked.
///
/// <para><b>Chronological-read index (Req 16.4).</b> <c>(CommunityId, UnitId, StartDate)</c> backs
/// "a unit's tenancy history in chronological order" as a single ordered indexed read (no N+1), and
/// the "find this resident's open tenancy for this unit" lookup the end-tenancy path runs.</para>
///
/// <para>No at-most-one-open constraint: a unit may have multiple concurrent open tenancies (co-
/// tenants), so ending a tenancy targets a specific resident's open row rather than closing all.
/// Prior entries are retained, never physically removed (Req 16.3). Relationships are <c>Restrict</c>
/// on delete; no inverse navigation is declared on <see cref="Resident"/>.</para>
/// </summary>
public sealed class TenancyHistoryEntityConfiguration : IEntityTypeConfiguration<TenancyHistory>
{
    public void Configure(EntityTypeBuilder<TenancyHistory> builder)
    {
        builder.ToTable("TenancyHistories");
        builder.HasKey(t => t.Id);

        // Tenant (resident) â†’ tenancy rows. Restrict: keep history traceable; soft delete archives.
        builder.HasOne(t => t.Resident)
            .WithMany()
            .HasForeignKey(t => t.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Link to the unit (Req 16.2). No navigation on Unit's side; restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(t => t.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological read + open-row lookup per unit (Req 16.4).
        builder.HasIndex(t => new { t.CommunityId, t.UnitId, t.StartDate })
            .HasDatabaseName("IX_TenancyHistory_CommunityId_UnitId_StartDate");
    }
}

/// <summary>
/// Maps the <see cref="Vehicle"/> table (Req 19.1, 19.2). As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4, 19.5) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident
/// principal only sees their own unit's vehicles.
///
/// <para><b>Concurrency-safe uniqueness (Req 19.2).</b> The unique index on
/// <c>(CommunityId, RegistrationNumber)</c> is the single source of truth for registration-number
/// uniqueness within a community: two concurrent registrations race to the index and exactly one
/// wins; the service lets the index reject the loser and maps the violation to CONFLICT
/// (<c>UniqueConstraintGuard</c>) rather than a read-then-write pre-check â€” the same approach as
/// <c>Unit(CommunityId, UnitNumber)</c>.</para>
///
/// <para>The <c>(CommunityId, UnitId)</c> index backs "a unit's vehicles" as a single indexed read
/// (no N+1, Req 19.5). The unit FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class VehicleEntityConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.RegistrationNumber).IsRequired().HasMaxLength(50);
        builder.Property(v => v.VehicleType).IsRequired().HasMaxLength(100);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(v => v.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // DB-enforced registration uniqueness within a community (Req 19.2): the concurrency-safe
        // source of truth, not an application read-then-write.
        builder.HasIndex(v => new { v.CommunityId, v.RegistrationNumber })
            .IsUnique()
            .HasDatabaseName("UX_Vehicle_CommunityId_RegistrationNumber");

        builder.HasIndex(v => new { v.CommunityId, v.UnitId })
            .HasDatabaseName("IX_Vehicle_CommunityId_UnitId");
    }
}

/// <summary>
/// Maps the <see cref="CommunicationPreference"/> per-resident, per-category opt-in row (Req 20.1,
/// Task 12.6). As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and
/// the rowversion mapping from <c>AppDbContext.OnModelCreating</c>.
///
/// <para><b>One row per (resident, category).</b> The <c>Unique(ResidentId, Category)</c> index is
/// the DB-level source of truth that each category has exactly one preference per resident (Req
/// 20.1); it also backs the Notification_Service's preference check as a single indexed lookup
/// (<c>ResidentId == x AND Category == y</c>), so there is no N+1 scan at scale.</para>
/// </summary>
public sealed class CommunicationPreferenceEntityConfiguration : IEntityTypeConfiguration<CommunicationPreference>
{
    public void Configure(EntityTypeBuilder<CommunicationPreference> builder)
    {
        builder.ToTable("CommunicationPreferences");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Category).IsRequired().HasMaxLength(100);

        // Resident â†’ communication preferences (one-to-many). Restrict: keep the preference traceable
        // with the resident; soft delete handles archival.
        builder.HasOne(p => p.Resident)
            .WithMany()
            .HasForeignKey(p => p.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // One preference row per (resident, category) â€” the invariant for Req 20.1 and the indexed
        // lookup path for the Notification_Service preference check.
        builder.HasIndex(p => new { p.ResidentId, p.Category })
            .IsUnique()
            .HasDatabaseName("UX_CommunicationPreference_ResidentId_Category");
    }
}

/// <summary>
/// Maps the <see cref="EmergencyContact"/> one-to-many child of a <see cref="Resident"/> (Req 15.3).
/// As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>. The <c>(CommunityId, ResidentId)</c>
/// index backs "a resident's emergency contacts" as a single indexed read.
/// </summary>
public sealed class EmergencyContactEntityConfiguration : IEntityTypeConfiguration<EmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmergencyContact> builder)
    {
        builder.ToTable("EmergencyContacts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Phone).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Relationship).IsRequired().HasMaxLength(100);

        // Resident â†’ emergency contacts (one-to-many). Restrict: contacts stay traceable with the
        // resident; soft delete handles archival.
        builder.HasOne(c => c.Resident)
            .WithMany(r => r.EmergencyContacts)
            .HasForeignKey(c => c.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.CommunityId, c.ResidentId })
            .HasDatabaseName("IX_EmergencyContact_CommunityId_ResidentId");
    }
}

public sealed class ResidentEntityConfiguration : IEntityTypeConfiguration<Resident>
{
    public void Configure(EntityTypeBuilder<Resident> builder)
    {
        builder.ToTable("Residents");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Email).HasMaxLength(256);
        builder.Property(r => r.Phone).HasMaxLength(50);
        builder.Property(r => r.ResidentType).IsRequired().HasMaxLength(100);
        builder.Property(r => r.VerificationStatus).IsRequired().HasMaxLength(50);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);
        // Optional photo reference (File_Service id). Nullable; 200 matches the File_Service
        // reference length used elsewhere.
        builder.Property(r => r.PhotoFileId).HasMaxLength(200);
        builder.Property(r => r.Gender).HasMaxLength(16);
        builder.Property(r => r.GovtIdType).HasMaxLength(32);
        builder.Property(r => r.GovtIdFrontFileId).HasMaxLength(200);
        builder.Property(r => r.GovtIdBackFileId).HasMaxLength(200);

        // Verification-queue list (Req 15.4): residents in a community by verification status.
        builder.HasIndex(r => new { r.CommunityId, r.VerificationStatus })
            .HasDatabaseName("IX_Resident_CommunityId_VerificationStatus");
    }
}
