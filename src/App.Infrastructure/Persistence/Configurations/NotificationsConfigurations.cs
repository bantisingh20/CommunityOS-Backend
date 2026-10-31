using App.Domain.Notifications;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Device"/> (design data-model table: <c>Device | Id, UserId, Platform,
/// OneSignalPlayerId, LastSeen</c>). <see cref="Device.OneSignalPlayerId"/> is Unique so a push
/// target maps to exactly one user â€” the data-layer guarantee behind notification confidentiality
/// (Req 11.3): a resolved notification can never be routed to another user's device. The
/// <c>(UserId, IsActive)</c> index backs the recipient resolver's lookup of a user's active devices.
/// Device is a platform-scoped entity (belongs to a user, not one community), so the OnModelCreating
/// loop applies the soft-delete filter and concurrency token automatically â€” no tenant predicate.
/// </summary>
public sealed class DeviceEntityConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");

        builder.Property(d => d.OneSignalPlayerId).IsRequired().HasMaxLength(128);
        builder.Property(d => d.Platform).HasConversion<int>();

        // One user per push target (confidentiality boundary, Req 11.3).
        builder.HasIndex(d => d.OneSignalPlayerId)
            .IsUnique()
            .HasDatabaseName("UX_Device_OneSignalPlayerId");

        // Recipient resolver path: a user's active devices.
        builder.HasIndex(d => new { d.UserId, d.IsActive })
            .HasDatabaseName("IX_Device_User_Active");
    }
}

/// <summary>
/// Maps <see cref="NotificationOutboxMessage"/> â€” the transactional-outbox table (Req 11.5). It is
/// a <c>TenantOwnedEntity</c>, so the AppDbContext OnModelCreating loop applies the tenant +
/// soft-delete global filter and the concurrency token automatically.
///
/// <para>The <c>(Status, NextAttemptUtc)</c> index is the Task 7.2 worker's drain path: fetch the
/// next batch of <see cref="OutboxStatus.Pending"/> rows whose <c>NextAttemptUtc</c> is due,
/// ordered by creation â€” so the drain query stays index-backed and fast at scale (Req 65.1).</para>
/// </summary>
public sealed class NotificationOutboxMessageEntityConfiguration : IEntityTypeConfiguration<NotificationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<NotificationOutboxMessage> builder)
    {
        builder.ToTable("NotificationOutbox");

        builder.Property(m => m.TemplateKey).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Category).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(400);
        builder.Property(m => m.Body).IsRequired().HasMaxLength(2000);
        builder.Property(m => m.PayloadJson).HasMaxLength(4000);
        builder.Property(m => m.TargetPlayerId).HasMaxLength(128);
        builder.Property(m => m.LastError).HasMaxLength(1000);
        builder.Property(m => m.Status).HasConversion<int>();
        builder.Property(m => m.CategoryKind).HasConversion<int>();
        builder.Property(m => m.Channels).HasConversion<int>();

        // Worker drain path (Task 7.2): due Pending rows, oldest first.
        builder.HasIndex(m => new { m.Status, m.NextAttemptUtc, m.CreatedAtUtc })
            .HasDatabaseName("IX_NotificationOutbox_Status_NextAttempt_Created");

        // Lookup a recipient's notifications (admin "sent notifications" view, tenant-filtered).
        builder.HasIndex(m => m.RecipientUserId)
            .HasDatabaseName("IX_NotificationOutbox_Recipient");

        // In-app inbox read path: a recipient's own notifications, newest first, with unread filter.
        builder.HasIndex(m => new { m.RecipientUserId, m.IsRead, m.CreatedAtUtc })
            .HasDatabaseName("IX_NotificationOutbox_Recipient_Read_Created");
    }
}
