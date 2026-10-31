using System.Reflection;
using App.Application.Abstractions;
using App.Application.Exceptions;
using App.Domain.Audit;
using App.Domain.Common;
using App.Domain.Community;
using App.Domain.Config;
using App.Domain.Files;
using App.Domain.Gate;
using App.Domain.Helpdesk;
using App.Domain.Idempotency;
using App.Domain.Identity;
using App.Domain.Maintenance;
using App.Domain.Notifications;
using App.Domain.Residents;
using App.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence;


public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    private readonly ITenantContext _tenant = tenant;

    public DbSet<SampleTenantRecord> SampleTenantRecords => Set<SampleTenantRecord>();

    public DbSet<Configuration> Configurations => Set<Configuration>();
    public DbSet<MasterDataItem> MasterDataItems => Set<MasterDataItem>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<OtpToken> OtpTokens => Set<OtpToken>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationOutboxMessage> NotificationOutbox => Set<NotificationOutboxMessage>();
    public DbSet<Device> Devices => Set<Device>();

    public DbSet<Domain.Messaging.MessageOutboxMessage> MessageOutbox => Set<Domain.Messaging.MessageOutboxMessage>();

    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Domain.Community.Community> Communities => Set<Domain.Community.Community>();
    public DbSet<HierarchyNode> HierarchyNodes => Set<HierarchyNode>();

    public DbSet<Announcement> Announcements => Set<Announcement>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
    public DbSet<ResidentJoinRequest> ResidentJoinRequests => Set<ResidentJoinRequest>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();

    public DbSet<CommunicationPreference> CommunicationPreferences => Set<CommunicationPreference>();

    public DbSet<OwnershipHistory> OwnershipHistories => Set<OwnershipHistory>();
    public DbSet<TenancyHistory> TenancyHistories => Set<TenancyHistory>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Pet> Pets => Set<Pet>();
    public DbSet<ParkingSlot> ParkingSlots => Set<ParkingSlot>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();


    public DbSet<VisitPass> VisitPasses => Set<VisitPass>();
    public DbSet<Visitor> Visitors => Set<Visitor>();

    public DbSet<RecurringVisitor> RecurringVisitors => Set<RecurringVisitor>();
    public DbSet<EntryExitEvent> EntryExitEvents => Set<EntryExitEvent>();

    public DbSet<WatchlistEntry> WatchlistEntries => Set<WatchlistEntry>();
    public DbSet<GuardShift> GuardShifts => Set<GuardShift>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<SosAlert> SosAlerts => Set<SosAlert>();
    public DbSet<AccessIntegrationEvent> AccessIntegrationEvents => Set<AccessIntegrationEvent>();

    public DbSet<Domain.Parcel.Parcel> Parcels => Set<Domain.Parcel.Parcel>();

    public DbSet<Domain.Parcel.CustodyLocation> CustodyLocations => Set<Domain.Parcel.CustodyLocation>();

    public DbSet<Domain.Parcel.ParcelAuthorization> ParcelAuthorizations => Set<Domain.Parcel.ParcelAuthorization>();
    public DbSet<Domain.Parcel.ParcelHandover> ParcelHandovers => Set<Domain.Parcel.ParcelHandover>();

    public DbSet<Domain.Parcel.CustodyTimelineEntry> CustodyTimelineEntries => Set<Domain.Parcel.CustodyTimelineEntry>();


    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();


    public DbSet<Asset> Assets => Set<Asset>();


    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderPart> WorkOrderParts => Set<WorkOrderPart>();
    public DbSet<WorkOrderLabour> WorkOrderLabour => Set<WorkOrderLabour>();
    public DbSet<EquipmentHistoryEntry> EquipmentHistoryEntries => Set<EquipmentHistoryEntry>();

    public DbSet<InspectionChecklist> InspectionChecklists => Set<InspectionChecklist>();
    public DbSet<InspectionChecklistItem> InspectionChecklistItems => Set<InspectionChecklistItem>();
    public DbSet<AmcContract> AmcContracts => Set<AmcContract>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();


    public DbSet<Domain.Amenities.Amenity> Amenities => Set<Domain.Amenities.Amenity>();
    public DbSet<Domain.Amenities.FacilitySlot> FacilitySlots => Set<Domain.Amenities.FacilitySlot>();
    public DbSet<Domain.Amenities.BookingRule> BookingRules => Set<Domain.Amenities.BookingRule>();
    public DbSet<Domain.Amenities.BlackoutDate> BlackoutDates => Set<Domain.Amenities.BlackoutDate>();

    public DbSet<Domain.Amenities.Booking> Bookings => Set<Domain.Amenities.Booking>();
    public DbSet<Domain.Amenities.SlotBookingCount> SlotBookingCounts => Set<Domain.Amenities.SlotBookingCount>();


    public DbSet<Domain.Amenities.ResidentEvent> ResidentEvents => Set<Domain.Amenities.ResidentEvent>();
    public DbSet<Domain.Amenities.ResidentEventRegistration> ResidentEventRegistrations => Set<Domain.Amenities.ResidentEventRegistration>();

    public DbSet<Domain.Parking.ParkingZone> ParkingZones => Set<Domain.Parking.ParkingZone>();
    public DbSet<Domain.Parking.ParkingAssignment> ParkingAssignments => Set<Domain.Parking.ParkingAssignment>();
    public DbSet<Domain.Parking.ParkingViolation> ParkingViolations => Set<Domain.Parking.ParkingViolation>();


    public DbSet<Domain.Parking.EvReservation> EvReservations => Set<Domain.Parking.EvReservation>();

    public DbSet<Domain.MaintenanceBilling.MaintenanceCharge> MaintenanceCharges => Set<Domain.MaintenanceBilling.MaintenanceCharge>();
    public DbSet<Domain.MaintenanceBilling.MaintenanceBill> MaintenanceBills => Set<Domain.MaintenanceBilling.MaintenanceBill>();
    public DbSet<Domain.MaintenanceBilling.MaintenancePayment> MaintenancePayments => Set<Domain.MaintenanceBilling.MaintenancePayment>();

    private bool IsSuperAdmin => _tenant.IsPlatformSuperAdmin;
    private bool IsResidentSelfScoped => _tenant.IsResidentSelfScoped;
    private Guid? ResidentUnitId => _tenant.ResidentUnitId;

    private static readonly MethodInfo TenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo ResidentScopedFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyResidentScopedTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo PlatformFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyPlatformFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<OwnershipHistory>()
                .HasIndex(o => o.UnitId)
                .IsUnique()
                .HasFilter("[EndDate] IS NULL")
                .HasDatabaseName("UX_OwnershipHistory_UnitId_Open");

            modelBuilder.Entity<Domain.Parcel.Parcel>()
                .HasIndex(p => p.CustodyLocationId)
                .IsUnique()
                .HasFilter("[CustodyLocationId] IS NOT NULL")
                .HasDatabaseName("UX_Parcel_CustodyLocationId_Active");

         
            modelBuilder.Entity<Domain.Parking.ParkingAssignment>()
                .HasIndex(a => a.ParkingSlotId)
                .IsUnique()
                .HasFilter("[EndedAtUtc] IS NULL")
                .HasDatabaseName("UX_ParkingAssignment_ParkingSlotId_Active");

          
            modelBuilder.Entity<Domain.Parking.EvReservation>()
                .HasIndex(r => r.ParkingSlotId)
                .IsUnique()
                .HasFilter("[EndedAtUtc] IS NULL")
                .HasDatabaseName("UX_EvReservation_ParkingSlotId_Active");

            modelBuilder.Entity<Domain.MaintenanceBilling.MaintenanceCharge>()
                .HasIndex(c => c.UnitId)
                .IsUnique()
                .HasFilter("[IsActive] = 1 AND [IsDeleted] = 0")
                .HasDatabaseName("UX_MaintenanceCharge_UnitId_Active");

            modelBuilder.Entity<Domain.MaintenanceBilling.MaintenanceBill>()
                .HasIndex(b => new { b.UnitId, b.PeriodYear, b.PeriodMonth })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [UnitId] IS NOT NULL")
                .HasDatabaseName("UX_MaintenanceBill_UnitId_Period");
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Optimistic concurrency token on both base types (Req 22.4).
            if (typeof(TenantOwnedEntity).IsAssignableFrom(clrType) ||
                typeof(PlatformEntity).IsAssignableFrom(clrType))
            {
                var rowVersion = modelBuilder.Entity(clrType)
                    .Property(nameof(TenantOwnedEntity.RowVersion));

                if (Database.IsSqlServer())
                {
                    // SQL Server generates & bumps an 8-byte rowversion automatically.
                    rowVersion.IsRowVersion();
                }
                else
                {
                    rowVersion.IsConcurrencyToken();
                }
            }

            if (typeof(TenantOwnedEntity).IsAssignableFrom(clrType))
            {
                var hasUnitScope = typeof(IUnitScoped).IsAssignableFrom(clrType);

                var method = hasUnitScope ? ResidentScopedFilterMethod : TenantFilterMethod;
                method.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
            }
            else if (typeof(PlatformEntity).IsAssignableFrom(clrType))
            {
                PlatformFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
            }
        }
    }

   
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "The record was modified by another request. Reload and try again.");
        }
    }


    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : TenantOwnedEntity =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            !e.IsDeleted &&
            (IsSuperAdmin || _tenant.AuthorizedCommunityIds.Contains(e.CommunityId)));


    private void ApplyResidentScopedTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : TenantOwnedEntity, IUnitScoped =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            !e.IsDeleted &&
            (IsSuperAdmin || _tenant.AuthorizedCommunityIds.Contains(e.CommunityId)) &&
            (!IsResidentSelfScoped || e.UnitId == ResidentUnitId));

    private void ApplyPlatformFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : PlatformEntity =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
}
