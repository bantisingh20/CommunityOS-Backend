using App.Domain.Amenities;
using App.Domain.Community;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Amenity"/> table (Req 38.1) â€” the bookable-amenity master. As a
/// <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a soft-deleted/
/// retired amenity stays traceable but is hidden from ordinary reads. It is deliberately NOT
/// <c>IUnitScoped</c> â€” an amenity is community infrastructure (same stance as
/// <see cref="AssetEntityConfiguration"/>).
///
/// <para><b>Community-unique code (Req 38.1).</b> The unique index on <c>(CommunityId, Code)</c> is
/// the DB-enforced identity of an amenity within a community, so a duplicate maps to CONFLICT via
/// <c>UniqueConstraintGuard</c> rather than a read-then-write pre-check. The
/// <c>(CommunityId, Status, AmenityType)</c> index backs the register's status/type-filtered list
/// (Req 38.1) as a single indexed read (no N+1).</para>
/// </summary>
public sealed class AmenityEntityConfiguration : IEntityTypeConfiguration<Amenity>
{
    public void Configure(EntityTypeBuilder<Amenity> builder)
    {
        builder.ToTable("Amenities");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Code).IsRequired().HasMaxLength(100);
        builder.Property(a => a.AmenityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Status).IsRequired().HasMaxLength(50);
        builder.Property(a => a.Location).HasMaxLength(300);
        builder.Property(a => a.Description).HasMaxLength(2000);

        // Code uniqueness within a community (Req 38.1): the DB-level source of truth, so a duplicate
        // maps to CONFLICT via UniqueConstraintGuard rather than a read-then-write pre-check.
        builder.HasIndex(a => new { a.CommunityId, a.Code })
            .IsUnique()
            .HasDatabaseName("UX_Amenity_CommunityId_Code");

        // The register's status/type-filtered list (Req 38.1) as a single indexed read, no N+1.
        builder.HasIndex(a => new { a.CommunityId, a.Status, a.AmenityType })
            .HasDatabaseName("IX_Amenity_CommunityId_Status_AmenityType");
    }
}

/// <summary>
/// Maps the <see cref="BlackoutDate"/> table (Req 38.4) â€” a date/period an amenity is unavailable
/// for booking. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and
/// the rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a
/// lifted (soft-deleted) blackout stays traceable but is hidden from ordinary reads. It is
/// deliberately NOT <c>IUnitScoped</c> â€” blackout configuration is community infrastructure.
///
/// <para><b>Blackout window index (Req 38.4).</b> <c>(CommunityId, AmenityId, StartUtc)</c> backs
/// both the "an amenity's blackout ranges, ordered by start" read and the 23.2 lifecycle's
/// range-overlap check (which blackouts cover a candidate booking instant) as a single ordered
/// indexed read (no scan, no N+1). No uniqueness is placed on the range â€” overlapping maintenance
/// windows are deliberately allowed. The amenity FK is <c>Restrict</c> on delete; deletes are soft
/// deletes anyway.</para>
/// </summary>
public sealed class BlackoutDateEntityConfiguration : IEntityTypeConfiguration<BlackoutDate>
{
    public void Configure(EntityTypeBuilder<BlackoutDate> builder)
    {
        builder.ToTable("BlackoutDates");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Reason).IsRequired().HasMaxLength(500);

        // Link to the owning amenity (Req 38.4). Restrict on delete; deletes are soft deletes anyway.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(b => b.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        // The amenity's blackout ranges ordered by start + the lifecycle overlap check (Req 38.4) as
        // a single ordered indexed read.
        builder.HasIndex(b => new { b.CommunityId, b.AmenityId, b.StartUtc })
            .HasDatabaseName("IX_BlackoutDate_CommunityId_AmenityId_StartUtc");
    }
}

/// <summary>
/// Maps the <see cref="Booking"/> table (Task 23.2, Req 39.1-39.5) â€” a resident's reservation of a
/// <see cref="FacilitySlot"/> for a specific date. As a <c>TenantOwnedEntity</c> it inherits the
/// tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; because it implements <c>IUnitScoped</c> the DbContext also
/// applies the resident self-scope filter (keyed on <see cref="Booking.UnitId"/>), so a resident
/// principal reaches only their OWN unit's bookings â€” a resident books/cancels only their own
/// bookings with no bespoke predicate (same stance as <c>Ticket</c> / <c>ParcelAuthorization</c>).
///
/// <para><b>Indexes (steering: no N+1, scalable reads).</b>
/// <c>(CommunityId, FacilitySlotId, BookingDate)</c> backs the "a slot's bookings for a date" read
/// (and keeps a staff booking list ordered/filterable without a scan).
/// <c>(CommunityId, ResidentId, BookingDate)</c> backs the per-resident cap count (how many bookings
/// a resident holds in the rolling window, Req 38.3) and the resident's own booking history as a
/// single indexed read. Money fields are decimals with explicit precision, never floats. The amenity
/// / slot FKs are <c>Restrict</c> on delete (deletes are soft deletes anyway).</para>
/// </summary>
public sealed class BookingEntityConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Status).IsRequired().HasMaxLength(50);
        builder.Property(b => b.BookingCharge).HasPrecision(18, 2);
        builder.Property(b => b.SecurityDeposit).HasPrecision(18, 2);

        // Link to the owning amenity + slot (Req 39.1). Restrict on delete; deletes are soft deletes.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(b => b.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FacilitySlot>()
            .WithMany()
            .HasForeignKey(b => b.FacilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // A slot's bookings for a date (Req 39.1) as a single ordered indexed read (no N+1/scan).
        builder.HasIndex(b => new { b.CommunityId, b.FacilitySlotId, b.BookingDate })
            .HasDatabaseName("IX_Booking_CommunityId_FacilitySlotId_BookingDate");

        // The per-resident cap count + the resident's own booking history (Req 38.3) as a single
        // indexed read â€” so the cap check never scans the table.
        builder.HasIndex(b => new { b.CommunityId, b.ResidentId, b.BookingDate })
            .HasDatabaseName("IX_Booking_CommunityId_ResidentId_BookingDate");
    }
}

/// <summary>
/// Maps the <see cref="BookingRule"/> table (Req 38.3) â€” the per-amenity configurable booking-rule
/// set. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped. It is
/// deliberately NOT <c>IUnitScoped</c> â€” rule configuration is community infrastructure.
///
/// <para><b>One rule-set per amenity (Req 38.3).</b> The unique index on <c>(AmenityId)</c> makes
/// "an amenity has at most one rule-set" a DB guarantee, so the service's upsert
/// (<c>SetBookingRuleAsync</c>) is race-safe â€” a concurrent duplicate insert maps to CONFLICT via
/// <c>UniqueConstraintGuard</c> rather than a read-then-write gap. The index is also the single
/// indexed read the rule lookup rides.</para>
///
/// <para><b>Money precision (steering).</b> <see cref="BookingRule.BookingCharge"/>,
/// <see cref="BookingRule.SecurityDeposit"/> and <see cref="BookingRule.RefundPercentOnCancellation"/>
/// are decimals mapped with explicit precision, never floats.</para>
/// </summary>
public sealed class BookingRuleEntityConfiguration : IEntityTypeConfiguration<BookingRule>
{
    public void Configure(EntityTypeBuilder<BookingRule> builder)
    {
        builder.ToTable("BookingRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.AllowedResidentTypes).HasMaxLength(500);
        builder.Property(r => r.BookingCharge).HasPrecision(18, 2);
        builder.Property(r => r.SecurityDeposit).HasPrecision(18, 2);
        builder.Property(r => r.RefundPercentOnCancellation).HasPrecision(5, 2);

        // Link to the owning amenity (Req 38.3). Restrict on delete; deletes are soft deletes anyway.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(r => r.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        // One rule-set per amenity (Req 38.3): the DB-level source of truth behind the upsert, so a
        // concurrent duplicate insert maps to CONFLICT rather than a read-then-write gap.
        builder.HasIndex(r => r.AmenityId)
            .IsUnique()
            .HasDatabaseName("UX_BookingRule_AmenityId");
    }
}

/// <summary>
/// Maps the <see cref="FacilitySlot"/> table (Req 38.2) â€” a bookable recurring time slot for an
/// amenity. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped and a
/// retired slot stays traceable but is hidden from ordinary reads. It is deliberately NOT
/// <c>IUnitScoped</c> â€” slot configuration is community infrastructure (same stance as
/// <see cref="AmenityEntityConfiguration"/>). The inherited <c>RowVersion</c> is the concurrency
/// token the 23.2 booking lifecycle rides when it books against the slot's capacity.
///
/// <para><b>Amenity slots index (Req 38.2).</b> <c>(CommunityId, AmenityId, StartMinutesOfDay)</c>
/// backs the "an amenity's slots, ordered by window start" read as a single ordered indexed read
/// (no N+1). The amenity FK is <c>Restrict</c> on delete so an amenity can't be hard-deleted out
/// from under its slots; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class FacilitySlotEntityConfiguration : IEntityTypeConfiguration<FacilitySlot>
{
    public void Configure(EntityTypeBuilder<FacilitySlot> builder)
    {
        builder.ToTable("FacilitySlots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(200);

        // Link to the owning amenity (Req 38.2). Restrict on delete; deletes are soft deletes anyway.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(s => s.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        // The amenity's slots list (Req 38.2) ordered by window start as a single indexed read.
        builder.HasIndex(s => new { s.CommunityId, s.AmenityId, s.StartMinutesOfDay })
            .HasDatabaseName("IX_FacilitySlot_CommunityId_AmenityId_StartMinutesOfDay");
    }
}

/// <summary>
/// Maps the <see cref="ResidentEventRegistration"/> table (Req 41.5, Task 24.2) â€” one resident's seat
/// on a <see cref="ResidentEvent"/>. As a <c>TenantOwnedEntity</c> AND <c>IUnitScoped</c>, the
/// <c>AppDbContext.OnModelCreating</c> loop wires the tenant + soft-delete global filter AND the
/// resident self-scope filter onto it (keyed on <c>UnitId</c> = the registering resident's unit), so
/// a resident principal sees only their OWN registrations; the rowversion guards the cancel (so a seat
/// release records exactly once).
///
/// <para><b>One registration per resident per event.</b> The <c>Unique(ResidentEventId, ResidentId)</c>
/// index makes "a resident registers for an event at most once" a DB guarantee â€” a concurrent
/// duplicate registration â†’ CONFLICT via <c>UniqueConstraintGuard</c>, no read-then-write existence
/// check. The at-capacity gate lives on the event's guarded <c>RegisteredCount</c> counter; this row
/// records the seat holder so a cancellation releases exactly one. The index is also the single
/// indexed read the registration flow uses to detect an existing registration.</para>
/// </summary>
public sealed class ResidentEventRegistrationEntityConfiguration : IEntityTypeConfiguration<ResidentEventRegistration>
{
    public void Configure(EntityTypeBuilder<ResidentEventRegistration> builder)
    {
        builder.ToTable("ResidentEventRegistrations");
        builder.HasKey(r => r.Id);

        // Link to the event (Req 41.5). Restrict on delete; deletes are soft deletes.
        builder.HasOne<ResidentEvent>()
            .WithMany()
            .HasForeignKey(r => r.ResidentEventId)
            .OnDelete(DeleteBehavior.Restrict);

        // One registration per resident per event (Req 41.5): the DB-level source of truth so a
        // concurrent duplicate registration maps to CONFLICT rather than a read-then-write check.
        builder.HasIndex(r => new { r.ResidentEventId, r.ResidentId })
            .IsUnique()
            .HasDatabaseName("UX_ResidentEventRegistration_EventId_ResidentId");
    }
}

/// <summary>
/// Maps the <see cref="SlotBookingCount"/> table (Task 23.2, Req 39.2, 39.5) â€” the per-(slot, date)
/// seat ledger the booking lifecycle reserves capacity against. As a <c>TenantOwnedEntity</c> it
/// inherits the tenant + soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>; it is deliberately NOT <c>IUnitScoped</c> â€” the counter is
/// capacity infrastructure shared across all residents booking the slot, not resident self-view data.
///
/// <para><b>One ledger row per (slot, date) â€” the capacity concurrency anchor (Req 39.5).</b> The
/// <c>Unique(FacilitySlotId, BookingDate)</c> index makes "one seat counter per slot per date" a DB
/// guarantee. The lifecycle creates the row lazily on the first booking for a (slot, date); two
/// concurrent first bookings both attempt the insert and the database lets exactly ONE succeed (the
/// loser's duplicate insert â†’ CONFLICT via <c>UniqueConstraintGuard</c>, re-read + retry). All
/// subsequent seat claims run the atomic guarded UPDATE
/// (<c>SET BookedCount = BookedCount + 1 WHERE BookedCount &lt; Capacity</c>) so two requests racing
/// the last seat serialize at the row â€” exactly one increments, no oversell, no read-then-write gap
/// (steering: concurrency safety via DB-level guarantees). The unique index is also the single
/// indexed read the lifecycle uses to resolve a (slot, date)'s counter.</para>
/// </summary>
public sealed class SlotBookingCountEntityConfiguration : IEntityTypeConfiguration<SlotBookingCount>
{
    public void Configure(EntityTypeBuilder<SlotBookingCount> builder)
    {
        builder.ToTable("SlotBookingCounts");
        builder.HasKey(c => c.Id);

        // Link to the owning amenity + slot (Req 39.2). Restrict on delete; deletes are soft deletes.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(c => c.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<FacilitySlot>()
            .WithMany()
            .HasForeignKey(c => c.FacilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);

        // One seat ledger per slot per date (Req 39.5): the DB-level source of truth behind the
        // lazy create-on-first-booking, so a concurrent duplicate insert maps to CONFLICT rather than
        // a read-then-write existence check â€” and the atomic guarded increment then enforces capacity.
        builder.HasIndex(c => new { c.FacilitySlotId, c.BookingDate })
            .IsUnique()
            .HasDatabaseName("UX_SlotBookingCount_FacilitySlotId_BookingDate");
    }
}

/// <summary>
/// Maps the <see cref="ResidentEvent"/> table (Req 41.4, 41.5, Task 24.2) â€” a resident-published
/// event with a schedule and a capacity. As a <c>TenantOwnedEntity</c> AND <c>IUnitScoped</c>, the
/// <c>AppDbContext.OnModelCreating</c> loop wires the tenant + soft-delete global filter AND the
/// resident self-scope filter onto it (keyed on <c>UnitId</c> = the organiser's host unit), so a
/// resident principal manages only their OWN unit's events; the rowversion guards edits.
///
/// <para><b>Capacity concurrency anchor (Req 41.5).</b> The event's inline
/// <c>RegisteredCount</c>/<c>Capacity</c> are the seat ledger: the registration flow claims a seat
/// with the atomic guarded UPDATE (<c>SET RegisteredCount = RegisteredCount + 1 WHERE RegisteredCount
/// &lt; Capacity</c>) so two residents racing the last seat serialize at the row â€” exactly one
/// registers, the other is rejected (no oversell, no read-then-write gap). The
/// <c>(CommunityId, UnitId)</c> index backs the organiser's "my events" read; the
/// <c>(CommunityId, StartsAtUtc)</c> index backs the community-wide upcoming-events browse.</para>
/// </summary>
public sealed class ResidentEventEntityConfiguration : IEntityTypeConfiguration<ResidentEvent>
{
    public void Configure(EntityTypeBuilder<ResidentEvent> builder)
    {
        builder.ToTable("ResidentEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Location).HasMaxLength(300);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(100);

        // Organiser's host unit (Req 41.4). Restrict on delete; deletes are soft deletes.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(e => e.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Organiser "my events" read (self-scope) (Req 41.4).
        builder.HasIndex(e => new { e.CommunityId, e.UnitId })
            .HasDatabaseName("IX_ResidentEvent_CommunityId_UnitId");

        // Community-wide upcoming-events browse (Req 41.4, 41.5).
        builder.HasIndex(e => new { e.CommunityId, e.StartsAtUtc })
            .HasDatabaseName("IX_ResidentEvent_CommunityId_StartsAtUtc");
    }
}
