using App.Domain.Community;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Domain.Community.Community"/> tenant-root table (Req 12.1). As a
/// <c>TenantOwnedEntity</c> it inherits the global tenant + soft-delete query filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>; because the row's own
/// <c>CommunityId</c> is set equal to its <c>Id</c> on create, that standard filter scopes the
/// Community row itself to the owning tenant with no bespoke predicate (see the modeling note on
/// the entity). Soft-delete keeps an archived Community traceable (Req 12.5).
/// </summary>
public sealed class CommunityEntityConfiguration : IEntityTypeConfiguration<Domain.Community.Community>
{
    public void Configure(EntityTypeBuilder<Domain.Community.Community> builder)
    {
        builder.ToTable("Communities");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.CommunityType).IsRequired().HasMaxLength(100);

        // "Which communities belong to this organization?" lookups.
        builder.HasIndex(c => c.OrganizationId)
            .HasDatabaseName("IX_Community_OrganizationId");

        builder.HasOne<Domain.Community.Organization>()
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps the <see cref="Announcement"/> table (MVP notices slice) â€” the community notice board. As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped (Req 5.2) and a
/// soft-deleted notice stays traceable but is hidden from ordinary reads â€” the same shape as
/// <see cref="AmenityEntityConfiguration"/>. It is deliberately NOT <c>IUnitScoped</c>: a notice is
/// community-wide, not pinned to one unit.
///
/// <para>The <c>(CommunityId, PublishedAtUtc)</c> index backs the board's newest-first list as a
/// single indexed read (no N+1).</para>
/// </summary>
public sealed class AnnouncementEntityConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Body).IsRequired().HasMaxLength(4000);
        builder.Property(a => a.Category).IsRequired().HasMaxLength(50);
        // Reversible board-visibility flag (admin activate/deactivate). Defaults to true.
        builder.Property(a => a.IsActive).HasDefaultValue(true);

        // The board's newest-first list as a single indexed read, no N+1.
        builder.HasIndex(a => new { a.CommunityId, a.PublishedAtUtc })
            .HasDatabaseName("IX_Announcement_CommunityId_PublishedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="HierarchyNode"/> self-referencing tree (Req 13.1). As a
/// <c>TenantOwnedEntity</c> it inherits the global tenant + soft-delete filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so hierarchy reads are tenant-scoped (Req 13.4)
/// and delete is a soft delete that stays traceable (Req 13.5). The <c>(CommunityId, ParentId)</c>
/// index backs the "load a community's whole hierarchy" read as a single indexed scan â€” no N+1.
/// </summary>
public sealed class HierarchyNodeEntityConfiguration : IEntityTypeConfiguration<HierarchyNode>
{
    public void Configure(EntityTypeBuilder<HierarchyNode> builder)
    {
        builder.ToTable("HierarchyNodes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Name).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Level).IsRequired().HasMaxLength(100);

        // Self-reference: parent â†’ children, both within the same community (enforced in the
        // service). Restrict so a parent cannot be hard-deleted out from under its children; delete
        // is a soft delete anyway.
        builder.HasOne(n => n.Parent)
            .WithMany(n => n.Children)
            .HasForeignKey(n => n.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Primary read path: fetch a community's hierarchy (optionally a subtree by parent).
        builder.HasIndex(n => new { n.CommunityId, n.ParentId })
            .HasDatabaseName("IX_HierarchyNode_CommunityId_ParentId");
    }
}

/// <summary>
/// Maps the platform-scoped <see cref="Organization"/> table (Req 12.1). As a
/// <c>PlatformEntity</c> it inherits the soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c> â€” no tenant predicate (an Organization owns communities, it
/// is not owned by one).
/// </summary>
public sealed class OrganizationEntityConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);
    }
}

/// <summary>
/// Maps the <see cref="Unit"/> table (Req 14.1â€“14.4). As a <c>TenantOwnedEntity</c> it inherits the
/// global tenant + soft-delete query filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so unit reads are tenant-scoped (Req 5.2), optimistic
/// concurrency on edit is enforced (Req 22.4) and delete is a soft delete that stays traceable
/// (Req 14.6).
///
/// <para><b>Concurrency-safe uniqueness (Req 14.4, 22.1).</b> The unique index on
/// <c>(CommunityId, UnitNumber)</c> is the single source of truth for unit-number uniqueness: two
/// concurrent creates race to the index and exactly one wins. The service does NOT pre-check with a
/// read-then-write; it lets the index reject the loser and translates the violation to a CONFLICT
/// (via <c>UniqueConstraintGuard</c>).</para>
///
/// <para><b>List-filter index (Req 14.5).</b> <c>(CommunityId, UnitStatus)</c> backs the
/// "units in this community with status X" list the Task 11.3 endpoint runs, so it hits an index
/// at scale.</para>
///
/// <para><b>Custom attributes (Req 14.1).</b> <see cref="Unit.CustomAttributes"/> is persisted as a
/// single JSON string column via a value converter â€” provider-agnostic (works the same on the SQL
/// Server production path and the SQLite test path, neither needing native JSON-column support) and
/// free-form, so a community can store arbitrary key/values without a schema change.</para>
/// </summary>
public sealed class UnitEntityConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.UnitNumber).IsRequired().HasMaxLength(100);
        builder.Property(u => u.UnitType).IsRequired().HasMaxLength(100);
        builder.Property(u => u.UnitStatus).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Floor).HasMaxLength(100);
        builder.Property(u => u.Area).HasColumnType("decimal(18,2)");

        // Free-form custom attributes stored as JSON (Req 14.1). A value converter keeps the SQL
        // Server and SQLite paths identical; a value comparer lets EF detect in-place dictionary
        // edits so an attribute change is persisted.
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        builder.Property(u => u.CustomAttributes)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => string.IsNullOrWhiteSpace(v)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(v, jsonOptions) ?? new Dictionary<string, string>())
            .Metadata.SetValueComparer(new ValueComparer<IDictionary<string, string>>(
                (a, b) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(b, jsonOptions),
                v => v == null ? 0 : JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
                v => v == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(v)));
        builder.Property(u => u.CustomAttributes).HasColumnName("CustomAttributes");

        // Optional link to the Floor/Building/â€¦ node (Req 14.1). Restrict so a node cannot be hard-
        // deleted out from under its units; delete is a soft delete anyway.
        builder.HasOne<HierarchyNode>()
            .WithMany()
            .HasForeignKey(u => u.HierarchyNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        // DB-enforced unit-number uniqueness within a community (Req 14.4, 22.1): two concurrent
        // creates race to this index and exactly one wins â€” the single source of truth, not an
        // application read-then-write. ponytail: a soft-deleted number is not reusable while the
        // archived row remains; reuse-after-archive would need a filtered index (SQL Server only) and
        // isn't a Task 11.2 requirement.
        builder.HasIndex(u => new { u.CommunityId, u.UnitNumber })
            .IsUnique()
            .HasDatabaseName("UX_Unit_CommunityId_UnitNumber");

        // List-filter index (Req 14.5): units in a community by status.
        builder.HasIndex(u => new { u.CommunityId, u.UnitStatus })
            .HasDatabaseName("IX_Unit_CommunityId_UnitStatus");
    }
}
