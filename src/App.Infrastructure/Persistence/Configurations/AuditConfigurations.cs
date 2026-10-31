using App.Domain.Audit;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the append-only <see cref="AuditLog"/> table (Req 6). This configuration <i>is</i> the
/// schema definition picked up by <c>ApplyConfigurationsFromAssembly</c> â€” the same mechanism that
/// maps Configuration/MasterDataItem â€” so <c>EnsureCreated</c> (tests) and any future
/// <c>Add-Migration</c> produce the table, columns and indexes from this one place (steering:
/// index names/config live in EF config, not magic strings).
///
/// <para>Two indexes per the design's AuditLog spec:
/// <list type="bullet">
///   <item><c>IX_AuditLog_CommunityId_Timestamp</c> â€” the tenant-scoped, time-ordered read path
///   (Req 6.5): filter by community then page newest-first;</item>
///   <item><c>IX_AuditLog_ResourceType_ResourceId</c> â€” "what happened to this resource?" lookups,
///   including entries whose referenced resource has since been soft-deleted (Req 6.4).</item>
/// </list></para>
/// </summary>
public sealed class AuditLogEntityConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActingUserId).HasMaxLength(200);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.ResourceType).IsRequired().HasMaxLength(200);
        builder.Property(a => a.ResourceId).HasMaxLength(200);
        // Detail is a small JSON/text blob (beforeâ†’after or security context); bounded but roomy.
        builder.Property(a => a.Detail).HasMaxLength(4000);

        // Tenant-scoped, time-ordered reads (Req 6.5).
        builder.HasIndex(a => new { a.CommunityId, a.TimestampUtc })
            .HasDatabaseName("IX_AuditLog_CommunityId_Timestamp");

        // Per-resource history, independent of the resource's soft-delete state (Req 6.4).
        builder.HasIndex(a => new { a.ResourceType, a.ResourceId })
            .HasDatabaseName("IX_AuditLog_ResourceType_ResourceId");
    }
}
