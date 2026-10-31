using App.Domain.Files;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="StoredFile"/> metadata table (Req 7.1). Picked up by
/// <c>ApplyConfigurationsFromAssembly</c> so <c>EnsureCreated</c> (tests) and any future migration
/// produce the table/columns/indexes from this one place (steering: schema in EF config, no magic
/// strings). As a <c>TenantOwnedEntity</c> it inherits the global tenant + soft-delete query filter
/// and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so no tenant predicate is
/// hand-wired here.
/// </summary>
public sealed class StoredFileEntityConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.OwningResourceType).IsRequired().HasMaxLength(200);
        builder.Property(f => f.FileName).IsRequired().HasMaxLength(400);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(200);
        builder.Property(f => f.StorageKey).IsRequired().HasMaxLength(400);

        // "Which files belong to this resource?" lookups, tenant-scoped.
        builder.HasIndex(f => new { f.CommunityId, f.OwningResourceType, f.OwningResourceId })
            .HasDatabaseName("IX_StoredFile_Community_Owner");
    }
}
