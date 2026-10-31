using App.Domain.Community;
using App.Domain.Files;
using App.Domain.Helpdesk;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Ticket"/> table (Req 32.1, 32.2, 32.4). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 32.4, 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident
/// only sees tickets for their own host unit, and the rowversion makes the Task 20.2/20.3 status
/// transitions record exactly once under concurrency.
///
/// <para>The <c>(CommunityId, UnitId, Status)</c> index backs the host unit's ticket list + the
/// resident self-view as a single indexed read (no N+1); <c>(CommunityId, Status)</c> backs the
/// staff status-filtered queue (e.g. new / in-progress). The host-unit FK is <c>Restrict</c> on
/// delete; deletes are soft deletes anyway. <c>HostUnitId</c> is a get/set alias over <c>UnitId</c>
/// and is not a stored column.</para>
/// </summary>
public sealed class TicketEntityConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(t => t.Id);

        // HostUnitId is a get/set alias over UnitId; not a stored column.
        builder.Ignore(t => t.HostUnitId);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(4000);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Subcategory).HasMaxLength(100);
        builder.Property(t => t.Priority).IsRequired().HasMaxLength(50);
        builder.Property(t => t.Status).IsRequired().HasMaxLength(50);

        // Assignment / SLA / escalation columns (Task 20.2, Req 33.1-33.3). All nullable â€” a ticket is
        // unassigned (and so has no SLA / escalation stamp) until it is assigned.
        builder.Property(t => t.AssignedToUserId);
        builder.Property(t => t.AssignedAtUtc);
        builder.Property(t => t.SlaDueAtUtc);
        builder.Property(t => t.SlaEscalatedAtUtc);

        // Resolution + feedback columns (Task 20.3, Req 34.3-34.5). All nullable â€” a ticket carries no
        // resolution instant until it is resolved, and no rating until the resident gives feedback on
        // their own resolved ticket. The comment is bounded like the description's shorter sibling.
        builder.Property(t => t.ResolvedAtUtc);
        builder.Property(t => t.Rating);
        builder.Property(t => t.FeedbackComment).HasMaxLength(2000);
        builder.Property(t => t.FeedbackAtUtc);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(t => t.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // The host unit's ticket list + the resident self-view list (Req 32.4, 5.4).
        builder.HasIndex(t => new { t.CommunityId, t.UnitId, t.Status })
            .HasDatabaseName("IX_Ticket_CommunityId_UnitId_Status");

        // The staff status-filtered queue (new / in-progress / resolved), Req 32.4.
        builder.HasIndex(t => new { t.CommunityId, t.Status })
            .HasDatabaseName("IX_Ticket_CommunityId_Status");

        // The SLA breach sweep (Task 20.2, Req 33.3): find assigned-but-unresolved tickets whose
        // SlaDueAtUtc has passed, bounded + ordered, without a table scan at scale. The sweep filters
        // on Status + SlaDueAtUtc within a community, so (CommunityId, Status, SlaDueAtUtc) is the
        // supporting index â€” mirrors the parcel custody-SLA sweep's indexing stance.
        builder.HasIndex(t => new { t.CommunityId, t.Status, t.SlaDueAtUtc })
            .HasDatabaseName("IX_Ticket_CommunityId_Status_SlaDueAtUtc");

        // Ticket analytics (Task 20.3, Req 34.5): the aggregate groups tickets by category + priority
        // within a community (counts / SLA compliance / resolution time), so (CommunityId, Category,
        // Priority) is the supporting index keeping the DB-side GROUP BY off a table scan at scale.
        // The by-status breakdown rides the (CommunityId, Status) index above.
        builder.HasIndex(t => new { t.CommunityId, t.Category, t.Priority })
            .HasDatabaseName("IX_Ticket_CommunityId_Category_Priority");
    }
}

/// <summary>
/// Maps the <see cref="TicketComment"/> table (helpdesk discussion thread). As a
/// <c>TenantOwnedEntity</c> that is also <c>IUnitScoped</c> it inherits the tenant + soft-delete +
/// resident self-scope global filter and the rowversion mapping from <c>AppDbContext.OnModelCreating</c>
/// â€” a resident only sees comments on tickets for their own unit.
///
/// <para>The <c>(CommunityId, TicketId, CreatedAtUtc)</c> index backs "list a ticket's comments in
/// order" as a single indexed read (no N+1). The ticket FK is <c>Restrict</c> on delete so a ticket
/// cannot be hard-deleted out from under its comments (deletes are soft deletes anyway).</para>
/// </summary>
public sealed class TicketCommentEntityConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("TicketComments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).IsRequired().HasMaxLength(2000);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        // Chronological "list a ticket's comments" read path.
        builder.HasIndex(c => new { c.CommunityId, c.TicketId, c.CreatedAtUtc })
            .HasDatabaseName("IX_TicketComment_CommunityId_TicketId_CreatedAtUtc");
    }
}

/// <summary>
/// Maps the <see cref="TicketAttachment"/> table (Req 32.3). As a <c>TenantOwnedEntity</c> that is
/// also <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 32.4, 5.4) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> â€” a resident
/// only sees attachments on tickets for their own unit.
///
/// <para>The <c>(CommunityId, TicketId)</c> index backs "list a ticket's attachments" as a single
/// indexed read (no N+1). The ticket FK is <c>Restrict</c> on delete so a ticket cannot be
/// hard-deleted out from under its attachments (deletes are soft deletes anyway). The attachment
/// references the File_Service <c>StoredFile</c> by <see cref="TicketAttachment.FileId"/>, with a
/// <c>Restrict</c> FK so the referenced file row cannot be hard-deleted while an attachment points at
/// it â€” the bytes stay reachable through the authorized download path (Req 7.2).</para>
/// </summary>
public sealed class TicketAttachmentEntityConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("TicketAttachments");
        builder.HasKey(a => a.Id);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(a => a.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<StoredFile>()
            .WithMany()
            .HasForeignKey(a => a.FileId)
            .OnDelete(DeleteBehavior.Restrict);

        // "List a ticket's attachments" + the resident self-view read path (Req 32.3).
        builder.HasIndex(a => new { a.CommunityId, a.TicketId })
            .HasDatabaseName("IX_TicketAttachment_CommunityId_TicketId");
    }
}
