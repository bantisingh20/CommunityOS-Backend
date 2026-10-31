using App.Domain.Idempotency;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="IdempotencyKey"/> (design "IdempotencyKey" table, Req 30.5, 44.2, 63.3). The
/// <b>Unique(KeyValue, Endpoint)</b> index is the correctness guarantee: it is what makes the
/// reserve-before-execute insert in <c>IdempotencyService</c> race-safe, so a concurrent duplicate
/// loses the insert and replays instead of executing a second time. The index also serves the
/// single-row replay lookup. No tenant/soft-delete filter applies â€” these rows are immutable
/// bookkeeping (the entity derives from no base, so the OnModelCreating loop skips it).
/// </summary>
public sealed class IdempotencyKeyEntityConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys");

        builder.Property(k => k.KeyValue).HasMaxLength(200).IsRequired();
        builder.Property(k => k.Endpoint).HasMaxLength(200).IsRequired();
        builder.Property(k => k.RequestHash).HasMaxLength(128).IsRequired();
        // ResponseSnapshot is the serialized original response; left as the provider default
        // (nvarchar(max) on SQL Server) so arbitrarily sized payloads round-trip.

        builder.HasIndex(k => new { k.KeyValue, k.Endpoint })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyKey_Key_Endpoint");
    }
}
