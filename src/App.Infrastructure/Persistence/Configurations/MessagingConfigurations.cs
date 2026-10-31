using App.Domain.Messaging;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

public sealed class MessageOutboxMessageEntityConfiguration : IEntityTypeConfiguration<MessageOutboxMessage>
{
    public void Configure(EntityTypeBuilder<MessageOutboxMessage> builder)
    {
        builder.ToTable("MessageOutbox");

        builder.Property(m => m.Channel).HasConversion<int>();
        builder.Property(m => m.Status).HasConversion<int>();
        builder.Property(m => m.Recipient).IsRequired().HasMaxLength(320);
        builder.Property(m => m.Subject).HasMaxLength(400);
        builder.Property(m => m.Body).IsRequired().HasMaxLength(8000);
        builder.Property(m => m.LastError).HasMaxLength(1000);

        builder.HasIndex(m => new { m.Status, m.NextAttemptUtc, m.CreatedAtUtc })
            .HasDatabaseName("IX_MessageOutbox_Status_NextAttempt_Created");
    }
}
