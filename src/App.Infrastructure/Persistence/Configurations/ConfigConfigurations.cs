using App.Application.Config;
using App.Application.Notifications;
using App.Domain.Config;
using App.Domain.Notifications;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Configuration"/>: required key/value, and the Unique(Key, Scope, CommunityId)
/// guarantee from Req 8.1. Because <c>CommunityId</c> is nullable and most relational providers
/// treat NULLs as distinct in a unique index (so several platform rows for the same key would
/// slip through), uniqueness is split into two <b>filtered</b> unique indexes:
/// one for platform rows (unique on <c>Key</c> where <c>CommunityId IS NULL</c>) and one for
/// community rows (unique on <c>(Key, CommunityId)</c> where <c>CommunityId IS NOT NULL</c>).
/// Both SQL Server and SQLite honour filtered indexes.
/// </summary>
public sealed class ConfigurationEntityConfiguration : IEntityTypeConfiguration<Configuration>
{
    public void Configure(EntityTypeBuilder<Configuration> builder)
    {
        builder.ToTable("Configurations");

        builder.Property(c => c.Key).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Value).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.Scope).HasConversion<int>();

        // Exactly one platform default per key (platform rows have BOTH ids null â€” an org row also
        // has CommunityId null, so the filter must exclude org rows too).
        builder.HasIndex(c => c.Key)
            .IsUnique()
            .HasFilter("[CommunityId] IS NULL AND [OrganizationId] IS NULL")
            .HasDatabaseName("UX_Configuration_Platform_Key");

        // Exactly one community override per (key, community).
        builder.HasIndex(c => new { c.Key, c.CommunityId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NOT NULL")
            .HasDatabaseName("UX_Configuration_Community_Key");

        // Exactly one organization override per (key, organization).
        builder.HasIndex(c => new { c.Key, c.OrganizationId })
            .IsUnique()
            .HasFilter("[OrganizationId] IS NOT NULL")
            .HasDatabaseName("UX_Configuration_Organization_Key");

        // Primary lookup path: resolve a key for a community (or platform) fast.
        builder.HasIndex(c => new { c.CommunityId, c.Key })
            .HasDatabaseName("IX_Configuration_CommunityId_Key");
    }
}

/// <summary>
/// Maps <see cref="MasterDataItem"/> and seeds the DEFAULT platform reference lists so the system
/// works out of the box (Req 8.4, 66.4). The seeded values are plain data rows an admin can edit,
/// add to or retire â€” they are not compiled enums that gate behaviour. Uniqueness is
/// Unique(ListKey, Scope, CommunityId, Code), again split into filtered platform/community indexes
/// for the nullable <c>CommunityId</c>.
/// </summary>
public sealed class MasterDataItemEntityConfiguration : IEntityTypeConfiguration<MasterDataItem>
{
    public void Configure(EntityTypeBuilder<MasterDataItem> builder)
    {
        builder.ToTable("MasterDataItems");

        builder.Property(m => m.ListKey).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Code).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Label).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Scope).HasConversion<int>();

        // One platform default per (list, code).
        builder.HasIndex(m => new { m.ListKey, m.Code })
            .IsUnique()
            .HasFilter("[CommunityId] IS NULL")
            .HasDatabaseName("UX_MasterData_Platform_ListKey_Code");

        // One community entry per (list, code, community).
        builder.HasIndex(m => new { m.ListKey, m.Code, m.CommunityId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NOT NULL")
            .HasDatabaseName("UX_MasterData_Community_ListKey_Code");

        // Lookup path: resolve a whole list for a community (or platform) in order.
        builder.HasIndex(m => new { m.CommunityId, m.ListKey, m.SortOrder })
            .HasDatabaseName("IX_MasterData_CommunityId_ListKey_SortOrder");

        builder.HasData(SeedDefaults());
    }

    /// <summary>
    /// Default platform-wide Unit_Type and Unit_Status lists (Req 8.4). Fixed GUIDs so the seed is
    /// idempotent across migrations/EnsureCreated. These are the values the Phase 2 Unit entity
    /// will validate against â€” resolved at runtime from this list, not hardcoded.
    /// </summary>
    private static IEnumerable<MasterDataItem> SeedDefaults()
    {
        // Unit_Type (Req 14.2: apartment, villa, bungalow, townhouse, plot, parking).
        var unitTypes = new (string Code, string Label)[]
        {
            ("apartment", "Apartment"),
            ("villa", "Villa"),
            ("bungalow", "Bungalow"),
            ("townhouse", "Townhouse"),
            ("plot", "Plot"),
            ("parking", "Parking"),
        };

        // Unit_Status (Req 14.3: Available, Occupied, Vacant, Under Maintenance, Blocked).
        var unitStatuses = new (string Code, string Label)[]
        {
            ("available", "Available"),
            ("occupied", "Occupied"),
            ("vacant", "Vacant"),
            ("under_maintenance", "Under Maintenance"),
            ("blocked", "Blocked"),
        };

        // Community_Type (Req 12.2): the configurable community classifications. Seeded as editable
        // data, not a compiled enum â€” an operator can add/retire a type without a code change.
        var communityTypes = new (string Code, string Label)[]
        {
            ("apartment_society", "Apartment Society"),
            ("gated_community", "Gated Community"),
            ("villa_bungalow", "Villa / Bungalow Community"),
            ("townhouse", "Townhouse Development"),
            ("mixed", "Mixed Development"),
        };

        // Announcement_Category (notice board): the configurable notice classifications. Editable
        // per community, validated at runtime, not a frozen enum.
        var announcementCategories = new (string Code, string Label)[]
        {
            ("general", "General"),
            ("event", "Event"),
            ("maintenance", "Maintenance"),
            ("emergency", "Emergency"),
        };

        // Hierarchy_Level (Req 13.1, 13.2): the optional property-hierarchy levels. Any may be used
        // or omitted; a HierarchyNode's level is validated against this editable list, not an enum.
        var hierarchyLevels = new (string Code, string Label)[]
        {
            ("phase", "Phase"),
            ("building_tower", "Building / Tower"),
            ("block", "Block"),
            ("zone", "Zone"),
            ("floor", "Floor"),
        };

        // Resident_Type (Req 15.1): the configurable resident classifications. Seeded as editable
        // data, not a compiled enum â€” a Resident's type is validated against this list at runtime, so
        // an operator can add/retire a type without a code change (steering: no hardcoding).
        var residentTypes = new (string Code, string Label)[]
        {
            ("owner", "Owner"),
            ("co_owner", "Co-Owner"),
            ("tenant", "Tenant"),
            ("family_member", "Family Member"),
            // Staff/guard person records (a Security-role login, not a unit occupant). Lets the admin
            // list/filter security staff separately from residents.
            ("security", "Security / Guard"),
        };

        // Resident_Verification_Status (Req 15.1, 17.x): the resident verification states. Seeded so
        // the create-time default (pending) and the Task 12.3 verification workflow have a
        // configurable vocabulary rather than a frozen enum.
        var residentVerificationStatuses = new (string Code, string Label)[]
        {
            ("pending", "Pending"),
            ("verified", "Verified"),
            ("rejected", "Rejected"),
        };

        // Vehicle_Type (Req 19.1): the configurable vehicle classifications a registered Vehicle is
        // typed against. Editable data, not a compiled enum â€” an operator can add/retire a type
        // without a code change (steering: no hardcoding).
        var vehicleTypes = new (string Code, string Label)[]
        {
            ("two_wheeler", "Two Wheeler"),
            ("four_wheeler", "Four Wheeler"),
            ("bicycle", "Bicycle"),
            ("commercial", "Commercial"),
            ("other", "Other"),
        };

        // Pet_Type (Req 19.3): the configurable pet classifications a registered Pet is typed
        // against. Editable data, validated at runtime, not a frozen enum.
        var petTypes = new (string Code, string Label)[]
        {
            ("dog", "Dog"),
            ("cat", "Cat"),
            ("bird", "Bird"),
            ("other", "Other"),
        };

        // Notification_Category (Req 20.1, Task 12.6): the configurable categories a resident opts
        // into. Aligned with the Task 7.1 template categories and the Req 64 notification matrix.
        // Seeded as editable data, not a frozen enum â€” an operator can add/retire a category without
        // a code change, and later modules extend the list simply by seeding a new row. `account` and
        // `emergency` are listed for completeness but are MANDATORY categories the Notification_Service
        // always sends regardless of opt-in (Req 20.4); the rest are optional (Req 20.3).
        var notificationCategories = new (string Code, string Label)[]
        {
            (NotificationCategories.Account, "Account & Security"),
            (NotificationCategories.Emergency, "Emergency Alerts"),
            (NotificationCategories.Responder, "Security Responder Alerts"),
            (NotificationCategories.Announcement, "Announcements"),
            (NotificationCategories.Parcel, "Parcels & Deliveries"),
            (NotificationCategories.Complaint, "Complaints & Tickets"),
            (NotificationCategories.Invoice, "Invoices & Billing"),
            (NotificationCategories.FacilityBooking, "Facility Bookings"),
            (NotificationCategories.DocumentExpiry, "Document Expiry Reminders"),
            (NotificationCategories.Visitor, "Visitor Requests"),
            (NotificationCategories.Parking, "Parking Alerts"),
            (NotificationCategories.Event, "Resident Events"),
        };

        // Visitor_Category (Req 24.1, 25.1, Task 16.1): the configurable walk-in/visit-pass visitor
        // classifications. Editable data, validated at runtime, not a frozen enum.
        var visitorCategories = new (string Code, string Label)[]
        {
            ("guest", "Guest"),
            ("cab", "Cab"),
            ("delivery", "Delivery"),
            ("service_provider", "Service Provider"),
            ("contractor", "Contractor"),
        };

        // ID_Proof_Type: the configurable identity-document types a walk-in visitor's ID is
        // classified as. Captured for future reference, validated at runtime, not a frozen enum.
        var idProofTypes = new (string Code, string Label)[]
        {
            ("aadhaar", "Aadhaar Card"),
            ("pan", "PAN Card"),
            ("driving_license", "Driving License"),
            ("voter_id", "Voter ID"),
            ("passport", "Passport"),
            ("other", "Other"),
        };

        // Visit_Pass_Status (Req 23.4, Task 16.1): the pre-approved pass states. Create defaults to
        // active; verification exhausts to used, the window drives expired, the host revokes.
        var visitPassStatuses = new (string Code, string Label)[]
        {
            ("active", "Active"),
            ("used", "Used"),
            ("expired", "Expired"),
            ("revoked", "Revoked"),
        };

        // Visitor_Status (Req 24.3, 24.4, Task 16.1): the walk-in approval states. Create defaults to
        // pending (entry withheld); the resident approves/rejects; entered/exited are Task 16.2.
        var visitorStatuses = new (string Code, string Label)[]
        {
            ("pending", "Pending"),
            ("approved", "Approved"),
            ("rejected", "Rejected"),
            ("expired", "Expired"),
            ("entered", "Entered"),
            ("exited", "Exited"),
        };

        // Recurring_Visitor_Status (Req 25.2, Task 16.2): the recurring-authorization states. Create
        // defaults to active (authorizes matching dates); the host resident revokes.
        var recurringVisitorStatuses = new (string Code, string Label)[]
        {
            ("active", "Active"),
            ("revoked", "Revoked"),
        };

        // --- Gate security operations (Task 16.3, Req 26.x, 27.x) ---------------------------------

        // Watchlist_Subject_Type (Req 26.1): what a watchlist entry matches on.
        var watchlistSubjectTypes = new (string Code, string Label)[]
        {
            ("person", "Person"),
            ("vehicle", "Vehicle"),
        };

        // Watchlist_Severity (Req 26.1): watchlist entry severities.
        var watchlistSeverities = new (string Code, string Label)[]
        {
            ("low", "Low"),
            ("medium", "Medium"),
            ("high", "High"),
            ("critical", "Critical"),
        };

        // Watchlist_Status (Req 26.5): active flags at the gate; cleared is retained but no longer flags.
        var watchlistStatuses = new (string Code, string Label)[]
        {
            ("active", "Active"),
            ("cleared", "Cleared"),
        };

        // Guard_Shift_Status (Req 26.4): roster states. Create defaults to scheduled.
        var guardShiftStatuses = new (string Code, string Label)[]
        {
            ("scheduled", "Scheduled"),
            ("active", "Active"),
            ("completed", "Completed"),
            ("cancelled", "Cancelled"),
        };

        // Incident_Category (Req 27.1): configurable incident classifications.
        var incidentCategories = new (string Code, string Label)[]
        {
            ("trespass", "Trespass"),
            ("theft", "Theft"),
            ("vandalism", "Vandalism"),
            ("medical", "Medical"),
            ("fire", "Fire"),
            ("dispute", "Dispute"),
            ("other", "Other"),
        };

        // Incident_Severity (Req 27.1, 27.2): the ORDER here IS the escalation rank (SortOrder) the
        // configurable threshold compares against (low=0, medium=1, high=2, critical=3). Keep
        // ascending by seriousness so a higher rank means a more severe incident.
        var incidentSeverities = new (string Code, string Label)[]
        {
            ("low", "Low"),
            ("medium", "Medium"),
            ("high", "High"),
            ("critical", "Critical"),
        };

        // Incident_Status (Req 27.1): report defaults to open; acknowledge/escalate/resolve transitions.
        var incidentStatuses = new (string Code, string Label)[]
        {
            ("open", "Open"),
            ("acknowledged", "Acknowledged"),
            ("escalated", "Escalated"),
            ("resolved", "Resolved"),
        };

        // Incident_Escalation_State (Req 27.2): none until the severity meets the threshold.
        var incidentEscalationStates = new (string Code, string Label)[]
        {
            ("none", "Not Escalated"),
            ("escalated", "Escalated"),
        };

        // Sos_Alert_Status (Req 27.4): raise defaults to active; stays active until acknowledged + resolved.
        var sosAlertStatuses = new (string Code, string Label)[]
        {
            ("active", "Active"),
            ("acknowledged", "Acknowledged"),
            ("resolved", "Resolved"),
        };

        // Access_Integration_Action (Req 27.5): the hardware actions the integration seam exposes.
        var accessIntegrationActions = new (string Code, string Label)[]
        {
            ("open_barrier", "Open Boom Barrier"),
            ("close_barrier", "Close Boom Barrier"),
            ("cctv_snapshot", "CCTV Snapshot"),
            ("access_event", "Access-Control Event"),
        };

        // Access_Integration_Status (Req 27.5): the recorded outcome of a triggered integration event.
        var accessIntegrationStatuses = new (string Code, string Label)[]
        {
            ("triggered", "Triggered"),
            ("succeeded", "Succeeded"),
            ("failed", "Failed"),
        };

        // --- Parcel, Delivery & Custody (Task 18.1, Req 28.x, 29.3) -------------------------------

        // Parcel_Status (Req 28.2): the parcel lifecycle states. Registration defaults to received;
        // the Task 18.1 state machine drives transitions against the Parcel_Status_Transition list.
        var parcelStatuses = new (string Code, string Label)[]
        {
            ("expected", "Expected"),
            ("received", "Received"),
            ("owner_not_available", "Owner Not Available"),
            ("in_custody", "In Custody"),
            ("ready_for_pickup", "Ready for Pickup"),
            ("handed_over", "Handed Over"),
            ("returned", "Returned"),
            ("lost_damaged", "Lost / Damaged"),
        };

        // Parcel_Category (Req 28.1): configurable parcel classifications. Which of these are treated
        // as priority is itself configurable (parcel.priorityCategories, Req 29.3).
        var parcelCategories = new (string Code, string Label)[]
        {
            ("standard", "Standard"),
            ("medicine", "Medicine"),
            ("perishable", "Perishable"),
            ("urgent", "Urgent"),
            ("document", "Document"),
        };

        // Parcel_Condition (Req 28.1): the recorded package condition on receipt.
        var parcelConditions = new (string Code, string Label)[]
        {
            ("good", "Good"),
            ("damaged", "Damaged"),
            ("opened", "Opened / Tampered"),
            ("wet", "Wet"),
        };

        // Parcel_Status_Transition (Req 28.3): the ALLOWED status transitions, each code a `from->to`
        // pair of Parcel_Status codes. The state machine permits a transition ONLY when its pair is
        // present + active here, so an illegal transition is rejected without a hardcoded table and a
        // community can tune its lifecycle by editing this list (steering: no hardcoding). The Label
        // is a human-readable description of the edge. Terminal states (handed_over, returned,
        // lost_damaged) have no outgoing edges.
        var parcelStatusTransitions = new (string Code, string Label)[]
        {
            ("expected->received", "Expected â†’ Received"),
            ("received->in_custody", "Received â†’ In Custody"),
            ("received->owner_not_available", "Received â†’ Owner Not Available"),
            ("received->ready_for_pickup", "Received â†’ Ready for Pickup"),
            ("received->handed_over", "Received â†’ Handed Over"),
            ("received->lost_damaged", "Received â†’ Lost / Damaged"),
            ("owner_not_available->in_custody", "Owner Not Available â†’ In Custody"),
            ("owner_not_available->returned", "Owner Not Available â†’ Returned"),
            ("in_custody->ready_for_pickup", "In Custody â†’ Ready for Pickup"),
            ("in_custody->handed_over", "In Custody â†’ Handed Over"),
            ("in_custody->returned", "In Custody â†’ Returned"),
            ("in_custody->lost_damaged", "In Custody â†’ Lost / Damaged"),
            ("ready_for_pickup->handed_over", "Ready for Pickup â†’ Handed Over"),
            ("ready_for_pickup->in_custody", "Ready for Pickup â†’ In Custody"),
            ("ready_for_pickup->returned", "Ready for Pickup â†’ Returned"),
            ("ready_for_pickup->lost_damaged", "Ready for Pickup â†’ Lost / Damaged"),
        };

        // Parcel_Verification_Method (Req 30.2): the configurable ways a guard verifies the collector
        // at handover. A parcel authorization names one of these; the handover enforces it. Editable
        // per community so a community can add/retire a method without a code change.
        var parcelVerificationMethods = new (string Code, string Label)[]
        {
            ("otp", "OTP"),
            ("qr", "QR Code"),
            ("id", "ID Verification"),
            ("combination", "Combination (OTP + QR + ID)"),
        };

        // --- Helpdesk tickets (Task 20.1, Req 32.1, 32.2, 34.1) -----------------------------------

        // Ticket_Category (Req 32.1, 32.2): configurable complaint/request classifications. Editable
        // per community, validated at runtime, not a frozen enum.
        var ticketCategories = new (string Code, string Label)[]
        {
            ("plumbing", "Plumbing"),
            ("electrical", "Electrical"),
            ("housekeeping", "Housekeeping"),
            ("security", "Security"),
            ("common_area", "Common Area"),
            ("other", "Other"),
        };

        // Ticket_Subcategory (Req 32.1, 32.2): configurable refinement of a category. Optional on a
        // ticket; the list is a flat vocabulary (a categoryâ†’subcategory mapping is out of scope for
        // 20.1 â€” a subcategory is validated as a member of this editable list).
        var ticketSubcategories = new (string Code, string Label)[]
        {
            ("leak", "Leak"),
            ("blockage", "Blockage"),
            ("no_power", "No Power"),
            ("short_circuit", "Short Circuit"),
            ("cleaning", "Cleaning"),
            ("waste", "Waste Removal"),
            ("lighting", "Lighting"),
            ("other", "Other"),
        };

        // Ticket_Priority (Req 32.1, 32.2): configurable priorities. The Task 20.2 SLA timer resolves
        // its target against this list. Editable per community, not a frozen enum.
        var ticketPriorities = new (string Code, string Label)[]
        {
            ("low", "Low"),
            ("medium", "Medium"),
            ("high", "High"),
            ("urgent", "Urgent"),
        };

        // Ticket_Status (Req 34.1): the ticket lifecycle states. Creation defaults to new; the Task
        // 20.3 state machine drives transitions. Seeded now so the create-time default resolves
        // against a configurable vocabulary rather than a frozen enum.
        var ticketStatuses = new (string Code, string Label)[]
        {
            ("new", "New"),
            ("assigned", "Assigned"),
            ("in_progress", "In Progress"),
            ("waiting", "Waiting"),
            ("resolved", "Resolved"),
            ("closed", "Closed"),
            ("reopened", "Reopened"),
        };

        // Ticket_Status_Transition (Task 20.2, Req 34.1): the ALLOWED ticket status transitions, each
        // code a `from->to` pair of Ticket_Status codes. The state machine permits a transition ONLY
        // when its pair is present + active here, so an illegal transition is rejected without a
        // hardcoded table and a community can tune its lifecycle by editing this list (steering: no
        // hardcoding) â€” mirrors Parcel_Status_Transition. Task 20.2 needs new->assigned (assignment)
        // and *->assigned (re-assignment from any live status); the rest of the lifecycle edges
        // (in_progress / waiting / resolved / closed / reopened) are seeded so the Task 20.3 state
        // machine has its vocabulary ready. Terminal `closed` has only the reopen edge out.
        var ticketStatusTransitions = new (string Code, string Label)[]
        {
            ("new->assigned", "New â†’ Assigned"),
            ("assigned->in_progress", "Assigned â†’ In Progress"),
            ("assigned->waiting", "Assigned â†’ Waiting"),
            ("assigned->assigned", "Assigned â†’ Assigned (re-assign)"),
            ("in_progress->waiting", "In Progress â†’ Waiting"),
            ("in_progress->resolved", "In Progress â†’ Resolved"),
            ("in_progress->assigned", "In Progress â†’ Assigned (re-assign)"),
            ("waiting->in_progress", "Waiting â†’ In Progress"),
            ("waiting->resolved", "Waiting â†’ Resolved"),
            ("waiting->assigned", "Waiting â†’ Assigned (re-assign)"),
            ("resolved->closed", "Resolved â†’ Closed"),
            ("resolved->reopened", "Resolved â†’ Reopened"),
            ("closed->reopened", "Closed â†’ Reopened"),
            ("reopened->assigned", "Reopened â†’ Assigned"),
        };

        // --- Maintenance assets (Task 21.1, Req 35.1) ---------------------------------------------

        // Asset_Status (Req 35.1): the asset lifecycle states. Registration defaults to active.
        // Seeded as editable data, not a frozen enum â€” 21.1 validates an asset's status as a member
        // of this list (no transition allow-list for the asset lifecycle).
        var assetStatuses = new (string Code, string Label)[]
        {
            ("active", "Active"),
            ("under_maintenance", "Under Maintenance"),
            ("inactive", "Inactive"),
            ("retired", "Retired"),
            ("disposed", "Disposed"),
        };

        // Asset_Type (Req 35.1): configurable asset classifications. Editable per community,
        // validated at runtime, not a frozen enum.
        var assetTypes = new (string Code, string Label)[]
        {
            ("lift", "Lift / Elevator"),
            ("pump", "Pump"),
            ("dg_set", "Diesel Generator"),
            ("hvac", "HVAC"),
            ("fire_safety", "Fire Safety"),
            ("electrical", "Electrical"),
            ("plumbing", "Plumbing"),
            ("other", "Other"),
        };

        // Asset_Category (Req 35.1): optional configurable grouping an asset may fall under. Editable
        // per community, validated at runtime, not a frozen enum.
        var assetCategories = new (string Code, string Label)[]
        {
            ("common_area", "Common Area"),
            ("utility", "Utility"),
            ("safety", "Safety"),
            ("amenity", "Amenity"),
            ("other", "Other"),
        };

        // --- Maintenance work orders (Task 21.2, Req 36.1, 36.2) ----------------------------------

        // Maintenance_Plan_Frequency (Req 36.1): the configurable preventive-plan frequencies. The
        // concrete spacing in days for each is itself configurable (maintenance.plan.intervalDays.{f},
        // seeded platform defaults in ConfigurationEntityConfiguration); `custom` carries the plan's
        // own supplied interval. Editable per community, validated at runtime, not a frozen enum.
        var maintenancePlanFrequencies = new (string Code, string Label)[]
        {
            ("daily", "Daily"),
            ("weekly", "Weekly"),
            ("monthly", "Monthly"),
            ("quarterly", "Quarterly"),
            ("half_yearly", "Half-Yearly"),
            ("yearly", "Yearly"),
            ("custom", "Custom (fixed interval)"),
        };

        // Work_Order_Type (Req 36.1, 36.2): preventive (plan-generated) vs corrective (raised
        // directly). Editable per community, validated at runtime, not a frozen enum.
        var workOrderTypes = new (string Code, string Label)[]
        {
            ("preventive", "Preventive"),
            ("corrective", "Corrective"),
        };

        // Work_Order_Priority (Req 36.2): configurable work-order priorities. Editable per community,
        // not a frozen enum.
        var workOrderPriorities = new (string Code, string Label)[]
        {
            ("low", "Low"),
            ("medium", "Medium"),
            ("high", "High"),
            ("urgent", "Urgent"),
        };

        // Work_Order_Status (Req 36.1-36.5): the work-order lifecycle states. Creation/generation
        // defaults to open; the state machine drives transitions against the Work_Order_Status_Transition
        // list. Editable per community, not a frozen enum.
        var workOrderStatuses = new (string Code, string Label)[]
        {
            ("open", "Open"),
            ("assigned", "Assigned"),
            ("in_progress", "In Progress"),
            ("on_hold", "On Hold"),
            ("completed", "Completed"),
            ("cancelled", "Cancelled"),
        };

        // Work_Order_Status_Transition (Req 36.1-36.5): the ALLOWED work-order status transitions, each
        // code a `from->to` pair of Work_Order_Status codes. The state machine permits a transition
        // ONLY when its pair is present + active here, so an illegal transition is rejected without a
        // hardcoded table and a community can tune its lifecycle by editing this list (steering: no
        // hardcoding) â€” mirrors Ticket_Status_Transition / Parcel_Status_Transition. Assignment needs
        // open->assigned and *->assigned (re-assign from any live status); completion needs
        // in_progress->completed (and assigned/on_hold->completed for short jobs). Terminal states
        // (completed, cancelled) have no outgoing edges.
        var workOrderStatusTransitions = new (string Code, string Label)[]
        {
            ("open->assigned", "Open â†’ Assigned"),
            ("open->cancelled", "Open â†’ Cancelled"),
            ("assigned->in_progress", "Assigned â†’ In Progress"),
            ("assigned->on_hold", "Assigned â†’ On Hold"),
            ("assigned->assigned", "Assigned â†’ Assigned (re-assign)"),
            ("assigned->completed", "Assigned â†’ Completed"),
            ("assigned->cancelled", "Assigned â†’ Cancelled"),
            ("in_progress->on_hold", "In Progress â†’ On Hold"),
            ("in_progress->completed", "In Progress â†’ Completed"),
            ("in_progress->assigned", "In Progress â†’ Assigned (re-assign)"),
            ("in_progress->cancelled", "In Progress â†’ Cancelled"),
            ("on_hold->in_progress", "On Hold â†’ In Progress"),
            ("on_hold->assigned", "On Hold â†’ Assigned (re-assign)"),
            ("on_hold->completed", "On Hold â†’ Completed"),
            ("on_hold->cancelled", "On Hold â†’ Cancelled"),
        };

        // --- Maintenance inspections / AMC / meters (Task 21.3, Req 37.1, 37.2, 37.4) -------------

        // Inspection_Result (Req 37.1): the configurable result codes an InspectionChecklist and each
        // of its items are recorded against. Editable per community, validated at runtime, not a
        // frozen enum.
        var inspectionResults = new (string Code, string Label)[]
        {
            ("pass", "Pass"),
            ("fail", "Fail"),
            ("pass_with_observations", "Pass with Observations"),
            ("not_applicable", "Not Applicable"),
        };

        // Amc_Contract_Status (Req 37.2): the AMC contract lifecycle states. Creation defaults to
        // active; a contract becomes expired past its end date or cancelled early. Editable per
        // community, validated at runtime, not a frozen enum.
        var amcContractStatuses = new (string Code, string Label)[]
        {
            ("active", "Active"),
            ("expired", "Expired"),
            ("cancelled", "Cancelled"),
        };

        // Meter_Type (Req 37.4): the configurable equipment meter/counter types a MeterReading is
        // recorded for â€” identifies which counter on an asset a reading is for. Editable per community,
        // validated at runtime, not a frozen enum.
        var meterTypes = new (string Code, string Label)[]
        {
            ("running_hours", "Running Hours"),
            ("energy_kwh", "Energy (kWh)"),
            ("flow", "Flow"),
            ("cycles", "Cycles"),
        };

        // Meter_Unit (Req 37.4): the configurable measurement units a MeterReading is recorded in.
        // Editable per community, validated at runtime, not a frozen enum.
        var meterUnits = new (string Code, string Label)[]
        {
            ("hours", "Hours"),
            ("kwh", "kWh"),
            ("litres", "Litres"),
            ("cycles", "Cycles"),
        };

        // --- Amenities & bookings (Task 23.1, Req 38.1) -------------------------------------------

        // Amenity_Type (Req 38.1): the configurable bookable-amenity classifications. Editable per
        // community, validated at runtime, not a frozen enum â€” a community adds/retires a type (incl.
        // `custom` for anything that does not fit) without a code change (steering: no hardcoding).
        var amenityTypes = new (string Code, string Label)[]
        {
            ("clubhouse", "Clubhouse"),
            ("gym", "Gym"),
            ("pool", "Swimming Pool"),
            ("hall", "Hall"),
            ("court", "Sports Court"),
            ("garden", "Garden"),
            ("party_area", "Party Area"),
            ("custom", "Custom"),
        };

        // Amenity_Status (Req 38.1): the amenity lifecycle states. Creation defaults to active.
        // Seeded as editable data, not a frozen enum â€” 23.1 validates an amenity's status as a member
        // of this list (no transition allow-list for the amenity lifecycle). An inactive /
        // under_maintenance amenity is not bookable (the 23.2 lifecycle reads this).
        var amenityStatuses = new (string Code, string Label)[]
        {
            ("active", "Active"),
            ("inactive", "Inactive"),
            ("under_maintenance", "Under Maintenance"),
        };

        // Booking_Status (Task 23.2, Req 39.1, 39.3): the facility-booking lifecycle states. Creation
        // defaults to requested (approval-required amenity) or confirmed (auto-confirm); the 23.2
        // state machine drives transitions against the Booking_Status_Transition list. Editable per
        // community, not a frozen enum.
        var bookingStatuses = new (string Code, string Label)[]
        {
            ("requested", "Requested"),
            ("confirmed", "Confirmed"),
            ("cancelled", "Cancelled"),
            ("completed", "Completed"),
        };

        // Booking_Status_Transition (Task 23.2, Req 39.3, 39.4): the ALLOWED booking status
        // transitions, each code a `from->to` pair of Booking_Status codes. The state machine permits
        // a transition ONLY when its pair is present + active here, so an illegal transition is
        // rejected without a hardcoded table and a community can tune its lifecycle by editing this
        // list (steering: no hardcoding) â€” mirrors Ticket/Parcel/Work_Order_Status_Transition.
        // Approval needs requested->confirmed; cancellation needs requested->cancelled and
        // confirmed->cancelled; completion needs confirmed->completed. Terminal states (cancelled,
        // completed) have no outgoing edges.
        var bookingStatusTransitions = new (string Code, string Label)[]
        {
            ("requested->confirmed", "Requested â†’ Confirmed (approve)"),
            ("requested->cancelled", "Requested â†’ Cancelled"),
            ("confirmed->cancelled", "Confirmed â†’ Cancelled"),
            ("confirmed->completed", "Confirmed â†’ Completed"),
        };

        // --- Parking (Task 24.1, Req 40.1-40.4) ---------------------------------------------------

        // Parking_Zone_Type (Req 40.1): the configurable parking-zone classifications a ParkingZone
        // is typed against. Editable per community, validated at runtime, not a frozen enum.
        var parkingZoneTypes = new (string Code, string Label)[]
        {
            ("surface", "Surface"),
            ("basement", "Basement"),
            ("podium", "Podium / Stilt"),
            ("visitor", "Visitor"),
            ("ev", "EV Charging"),
        };

        // Parking_Slot_Type (Req 40.1): the configurable slot classifications a ParkingSlot is typed
        // against. Editable per community, validated at runtime, not a frozen enum. The visitor/ev
        // types are what the Task 24.2 guest-parking / EV-reservation flows draw their pool from.
        var parkingSlotTypes = new (string Code, string Label)[]
        {
            ("open", "Open"),
            ("covered", "Covered"),
            ("visitor", "Visitor"),
            ("ev", "EV Charging"),
            ("accessible", "Accessible"),
        };

        // Parking_Assignment_Type (Req 40.2): the configurable assignment classifications a
        // ParkingAssignment is typed against. Editable per community, validated at runtime, not a
        // frozen enum.
        var parkingAssignmentTypes = new (string Code, string Label)[]
        {
            ("permanent", "Permanent"),
            ("temporary", "Temporary"),
            ("visitor", "Visitor"),
        };

        // Parking_Violation_Type (Req 40.4): the configurable violation classifications a
        // ParkingViolation is typed against. Editable per community, validated at runtime, not a
        // frozen enum.
        var parkingViolationTypes = new (string Code, string Label)[]
        {
            ("wrong_slot", "Parked in Wrong Slot"),
            ("overstay", "Overstay"),
            ("blocking", "Blocking / Obstruction"),
            ("no_permit", "No Parking Permit"),
            ("unauthorized", "Unauthorized Vehicle"),
        };

        // Parking_Violation_Status (Req 40.4): the violation lifecycle states. Record defaults to
        // open; resolved/waived close it. Editable per community, validated at runtime, not a frozen
        // enum (no transition allow-list â€” the status is validated as a member of this list).
        var parkingViolationStatuses = new (string Code, string Label)[]
        {
            ("open", "Open"),
            ("resolved", "Resolved"),
            ("waived", "Waived"),
        };

        // --- Guest parking, EV charging & resident events (Task 24.2, Req 41.1-41.5) --------------

        // Ev_Reservation_Status (Req 41.2): the EV-reservation lifecycle states. Reserve defaults to
        // reserved; charging marks the in-progress session; completed/cancelled close the active row.
        // Editable per community, validated at runtime, not a frozen enum (no transition allow-list â€”
        // the status is validated as a member of this list).
        var evReservationStatuses = new (string Code, string Label)[]
        {
            ("reserved", "Reserved"),
            ("charging", "Charging"),
            ("completed", "Completed"),
            ("cancelled", "Cancelled"),
        };

        // Resident_Event_Status (Req 41.4): the resident-event lifecycle states. Publish defaults to
        // published; cancelled/completed are terminal. Editable per community, validated at runtime,
        // not a frozen enum (no transition allow-list â€” the status is validated as a member of this
        // list).
        var residentEventStatuses = new (string Code, string Label)[]
        {
            ("published", "Published"),
            ("cancelled", "Cancelled"),
            ("completed", "Completed"),
        };

        var maintenanceBillStatuses = new (string Code, string Label)[]
        {
            ("unpaid", "Unpaid"),
            ("partially_paid", "Partially Paid"),
            ("paid", "Paid"),
            ("waived", "Waived"),
        };

        var maintenancePaymentMethods = new (string Code, string Label)[]
        {
            ("cash", "Cash"),
            ("cheque", "Cheque"),
            ("upi", "UPI"),
            ("bank_transfer", "Bank Transfer"),
            ("online", "Online"),
        };

        var maintenancePaymentStatuses = new (string Code, string Label)[]
        {
            ("pending_approval", "Pending Approval"),
            ("approved", "Approved"),
            ("rejected", "Rejected"),
        };

        // Ticket_Assignment_Rule (Task 20.2, Req 33.1): NO platform defaults â€” assignment rules name a
        // community's OWN staff users (each code is a `category->userId` pair), so they are inherently
        // per-community data a community adds via Config_Service, not a platform seed. Declared here as
        // documentation of the key; see ConfigKeys/MasterDataKeys.TicketAssignmentRule. Absent any
        // rule, the ticket falls back to ticket.defaultAssigneeUserId or stays unassigned.

        var order = 0;
        foreach (var (code, label) in unitTypes)
        {
            yield return Platform(MasterDataKeys.UnitType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in unitStatuses)
        {
            yield return Platform(MasterDataKeys.UnitStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in communityTypes)
        {
            yield return Platform(MasterDataKeys.CommunityType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in announcementCategories)
        {
            yield return Platform(MasterDataKeys.AnnouncementCategory, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in hierarchyLevels)
        {
            yield return Platform(MasterDataKeys.HierarchyLevel, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in residentTypes)
        {
            yield return Platform(MasterDataKeys.ResidentType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in residentVerificationStatuses)
        {
            yield return Platform(MasterDataKeys.ResidentVerificationStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in vehicleTypes)
        {
            yield return Platform(MasterDataKeys.VehicleType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in petTypes)
        {
            yield return Platform(MasterDataKeys.PetType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in notificationCategories)
        {
            yield return Platform(MasterDataKeys.NotificationCategory, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in visitorCategories)
        {
            yield return Platform(MasterDataKeys.VisitorCategory, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in idProofTypes)
        {
            yield return Platform(MasterDataKeys.IdProofType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in visitPassStatuses)
        {
            yield return Platform(MasterDataKeys.VisitPassStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in visitorStatuses)
        {
            yield return Platform(MasterDataKeys.VisitorStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in recurringVisitorStatuses)
        {
            yield return Platform(MasterDataKeys.RecurringVisitorStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in watchlistSubjectTypes)
        {
            yield return Platform(MasterDataKeys.WatchlistSubjectType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in watchlistSeverities)
        {
            yield return Platform(MasterDataKeys.WatchlistSeverity, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in watchlistStatuses)
        {
            yield return Platform(MasterDataKeys.WatchlistStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in guardShiftStatuses)
        {
            yield return Platform(MasterDataKeys.GuardShiftStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in incidentCategories)
        {
            yield return Platform(MasterDataKeys.IncidentCategory, code, label, order++);
        }

        // Incident_Severity: SortOrder == escalation rank (low=0 â€¦ critical=3), Req 27.2.
        order = 0;
        foreach (var (code, label) in incidentSeverities)
        {
            yield return Platform(MasterDataKeys.IncidentSeverity, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in incidentStatuses)
        {
            yield return Platform(MasterDataKeys.IncidentStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in incidentEscalationStates)
        {
            yield return Platform(MasterDataKeys.IncidentEscalationState, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in sosAlertStatuses)
        {
            yield return Platform(MasterDataKeys.SosAlertStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in accessIntegrationActions)
        {
            yield return Platform(MasterDataKeys.AccessIntegrationAction, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in accessIntegrationStatuses)
        {
            yield return Platform(MasterDataKeys.AccessIntegrationStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parcelStatuses)
        {
            yield return Platform(MasterDataKeys.ParcelStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parcelCategories)
        {
            yield return Platform(MasterDataKeys.ParcelCategory, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parcelConditions)
        {
            yield return Platform(MasterDataKeys.ParcelCondition, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parcelStatusTransitions)
        {
            yield return Platform(MasterDataKeys.ParcelStatusTransition, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parcelVerificationMethods)
        {
            yield return Platform(MasterDataKeys.ParcelVerificationMethod, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in ticketCategories)
        {
            yield return Platform(MasterDataKeys.TicketCategory, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in ticketSubcategories)
        {
            yield return Platform(MasterDataKeys.TicketSubcategory, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in ticketPriorities)
        {
            yield return Platform(MasterDataKeys.TicketPriority, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in ticketStatuses)
        {
            yield return Platform(MasterDataKeys.TicketStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in ticketStatusTransitions)
        {
            yield return Platform(MasterDataKeys.TicketStatusTransition, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in assetStatuses)
        {
            yield return Platform(MasterDataKeys.AssetStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in assetTypes)
        {
            yield return Platform(MasterDataKeys.AssetType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in assetCategories)
        {
            yield return Platform(MasterDataKeys.AssetCategory, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in maintenancePlanFrequencies)
        {
            yield return Platform(MasterDataKeys.MaintenancePlanFrequency, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in workOrderTypes)
        {
            yield return Platform(MasterDataKeys.WorkOrderType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in workOrderPriorities)
        {
            yield return Platform(MasterDataKeys.WorkOrderPriority, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in workOrderStatuses)
        {
            yield return Platform(MasterDataKeys.WorkOrderStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in workOrderStatusTransitions)
        {
            yield return Platform(MasterDataKeys.WorkOrderStatusTransition, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in inspectionResults)
        {
            yield return Platform(MasterDataKeys.InspectionResult, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in amcContractStatuses)
        {
            yield return Platform(MasterDataKeys.AmcContractStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in meterTypes)
        {
            yield return Platform(MasterDataKeys.MeterType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in meterUnits)
        {
            yield return Platform(MasterDataKeys.MeterUnit, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in amenityTypes)
        {
            yield return Platform(MasterDataKeys.AmenityType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in amenityStatuses)
        {
            yield return Platform(MasterDataKeys.AmenityStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in bookingStatuses)
        {
            yield return Platform(MasterDataKeys.BookingStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in bookingStatusTransitions)
        {
            yield return Platform(MasterDataKeys.BookingStatusTransition, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parkingZoneTypes)
        {
            yield return Platform(MasterDataKeys.ParkingZoneType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parkingSlotTypes)
        {
            yield return Platform(MasterDataKeys.ParkingSlotType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parkingAssignmentTypes)
        {
            yield return Platform(MasterDataKeys.ParkingAssignmentType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parkingViolationTypes)
        {
            yield return Platform(MasterDataKeys.ParkingViolationType, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in parkingViolationStatuses)
        {
            yield return Platform(MasterDataKeys.ParkingViolationStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in evReservationStatuses)
        {
            yield return Platform(MasterDataKeys.EvReservationStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in residentEventStatuses)
        {
            yield return Platform(MasterDataKeys.ResidentEventStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in maintenanceBillStatuses)
        {
            yield return Platform(MasterDataKeys.MaintenanceBillStatus, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in maintenancePaymentMethods)
        {
            yield return Platform(MasterDataKeys.MaintenancePaymentMethod, code, label, order++);
        }

        order = 0;
        foreach (var (code, label) in maintenancePaymentStatuses)
        {
            yield return Platform(MasterDataKeys.MaintenancePaymentStatus, code, label, order++);
        }
    }

    private static MasterDataItem Platform(string listKey, string code, string label, int sortOrder) => new()
    {
        // Deterministic id derived from (listKey, code) so re-seeding is stable and never duplicates.
        Id = DeterministicGuid($"{listKey}|{code}"),
        ListKey = listKey,
        Scope = ConfigScope.Platform,
        CommunityId = null,
        Code = code,
        Label = label,
        SortOrder = sortOrder,
        IsActive = true,
        // Audit columns are non-nullable DateTime; give the seed a fixed, deterministic stamp.
        CreatedAtUtc = SeedTimestamp,
        CreatedBy = "seed",
        RowVersion = [],
    };

    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }
}

/// <summary>
/// Maps <see cref="NotificationTemplate"/> and seeds the DEFAULT platform templates so the resident
/// verification/approval notices work out of the box (Req 11.1, 11.2). Seeded rows are plain data
/// an admin can edit/retire or override per community â€” not compiled strings (steering: no
/// hardcoding). Uniqueness is Unique(Key, Scope, CommunityId), split into filtered platform/
/// community indexes for the nullable <c>CommunityId</c> â€” the same technique the Configuration /
/// MasterData maps use.
/// </summary>
public sealed class NotificationTemplateEntityConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplates");

        builder.Property(t => t.Key).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(100);
        builder.Property(t => t.TitleTemplate).IsRequired().HasMaxLength(400);
        builder.Property(t => t.BodyTemplate).IsRequired().HasMaxLength(2000);
        builder.Property(t => t.EmailSubjectTemplate).HasMaxLength(400);
        builder.Property(t => t.EmailHtmlTemplate).HasMaxLength(8000);
        builder.Property(t => t.SmsTemplate).HasMaxLength(1000);
        builder.Property(t => t.WhatsappTemplateName).HasMaxLength(200);
        builder.Property(t => t.WhatsappVarsJson).HasMaxLength(4000);
        builder.Property(t => t.Scope).HasConversion<int>();
        builder.Property(t => t.CategoryKind).HasConversion<int>();
        builder.Property(t => t.Channels).HasConversion<int>();

        // One platform default per key.
        builder.HasIndex(t => t.Key)
            .IsUnique()
            .HasFilter("[CommunityId] IS NULL")
            .HasDatabaseName("UX_NotificationTemplate_Platform_Key");

        // One community override per (key, community).
        builder.HasIndex(t => new { t.Key, t.CommunityId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NOT NULL")
            .HasDatabaseName("UX_NotificationTemplate_Community_Key");

        builder.HasData(SeedDefaults());
    }

    /// <summary>
    /// Default platform-wide resident verification/approval templates (Req 11.2). Classified
    /// <see cref="NotificationCategoryKind.Mandatory"/> (account/security) so they are always sent
    /// regardless of optional preferences (Req 20.4). Fixed, deterministic ids so the seed is
    /// idempotent across EnsureCreated/migrations. The wording lives here as editable data; later
    /// phases add the rest of the Req 64 matrix the same way.
    /// </summary>
    private static IEnumerable<NotificationTemplate> SeedDefaults()
    {
        // Signed out elsewhere â†’ the single-active-session notice to the OLD device being logged out
        // when the user signs in on a new device. MANDATORY "account" security notice â€” always
        // delivered regardless of opt-out (Req 20.4). Delivered to the specific old device via the
        // outbox TargetPlayerId. No placeholders.
        yield return Platform(
            NotificationTemplateKeys.SignedOutElsewhere,
            "Signed out",
            "You've been signed out because your account was used to sign in on another device. If this wasn't you, reset your password.");

        yield return Platform(
            NotificationTemplateKeys.ResidentVerificationApproved,
            "Verification approved",
            "Hi {residentName}, your resident verification has been approved. Your account is now active.");

        yield return Platform(
            NotificationTemplateKeys.ResidentVerificationRejected,
            "Verification update",
            "Hi {residentName}, your resident verification could not be approved. Reason: {reason}.");

        yield return Platform(
            NotificationTemplateKeys.ResidentApprovalApproved,
            "Account approved",
            "Hi {residentName}, your account for {communityName} has been approved.");

        // Resident self-onboarding â€” notify the community admins a request needs review.
        yield return Platform(
            NotificationTemplateKeys.JoinRequestSubmitted,
            "New resident request",
            "{name} has requested to join unit {unitNumber}. Review and approve or reject the request.");

        // Resident self-onboarding â€” tell the requester the outcome.
        yield return Platform(
            NotificationTemplateKeys.JoinRequestApproved,
            "Request approved",
            "Your request to join {communityName} has been approved. You can now sign in.");

        yield return Platform(
            NotificationTemplateKeys.JoinRequestRejected,
            "Request update",
            "Your request to join {communityName} could not be approved. {reason}");

        // Walk-in visitor request â†’ the host unit's residents approve/reject (Req 24.2, Task 16.1).
        // OPTIONAL "visitor" category (a resident can opt out, Req 20.3) â€” unlike the mandatory
        // account notices above.
        yield return Platform(
            NotificationTemplateKeys.VisitorRequest,
            "Visitor at the gate",
            "A {category} visitor, {visitorName}, is requesting to visit your unit. Please approve or reject.",
            NotificationCategories.Visitor,
            NotificationCategoryKind.Optional);

        // Walk-in APPROVED â†’ tells the GUARD who registered it to admit the visitor (Task 20261008).
        // Optional "visitor" category; fills {visitorName}, {unitNumber}.
        yield return Platform(
            NotificationTemplateKeys.VisitorApproved,
            "Walk-in approved",
            "The resident approved {visitorName} for unit {unitNumber}. You can admit the visitor.",
            NotificationCategories.Visitor,
            NotificationCategoryKind.Optional);

        // Walk-in REJECTED â†’ tells the GUARD who registered it not to admit (Task 20261008).
        yield return Platform(
            NotificationTemplateKeys.VisitorRejected,
            "Walk-in rejected",
            "The resident rejected {visitorName} for unit {unitNumber}. Do not admit the visitor. {reason}",
            NotificationCategories.Visitor,
            NotificationCategoryKind.Optional);

        // Walk-in EXPIRED â†’ tells the GUARD who registered it that it lapsed without approval.
        yield return Platform(
            NotificationTemplateKeys.VisitorExpired,
            "Walk-in expired",
            "The request for {visitorName} to unit {unitNumber} expired without approval. Re-register if the visitor is still waiting.",
            NotificationCategories.Visitor,
            NotificationCategoryKind.Optional);

        // Incident escalated to responders (Req 27.2, Task 16.3). MANDATORY "responder" category â€”
        // security staff must always receive an escalation regardless of opt-out (Req 20.4).
        yield return Platform(
            NotificationTemplateKeys.IncidentEscalated,
            "Security incident escalated",
            "A {severity} {category} incident has been escalated at {location}. Please respond.",
            NotificationCategories.Responder,
            NotificationCategoryKind.Mandatory);

        // SOS raised (Req 27.3, Task 16.3). MANDATORY "emergency" category â€” always delivered to the
        // configured audience regardless of opt-out (Req 20.4).
        yield return Platform(
            NotificationTemplateKeys.SosRaised,
            "Emergency SOS raised",
            "An emergency SOS has been raised at {location}. Immediate response required.",
            NotificationCategories.Emergency,
            NotificationCategoryKind.Mandatory);

        // Parcel received / taken into custody â†’ notify the recipient unit's residents (Req 29.2,
        // Task 18.2). OPTIONAL "parcel" category (a resident can opt out, Req 20.3). Req 64.2 says a
        // parcel-received notice targets push + SMS + email, so the template declares all three
        // channels (the Phase 1 worker dispatches push today; SMS/email light up in later phases
        // without a schema or seed change â€” NotificationDispatcher remarks).
        yield return Platform(
            NotificationTemplateKeys.ParcelReceived,
            "Parcel received",
            "A parcel ({provider}, {trackingNumber}) has been received for your unit and is held at {custodyLocation}.",
            NotificationCategories.Parcel,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Sms | NotificationChannel.Email);

        // Priority variant for a priority-flagged parcel â€” medicine / perishable / urgent (Req 29.3,
        // 64.2). Same optional "parcel" category and the full push + SMS + email channel set, with
        // priority wording so a time-sensitive delivery is surfaced prominently. The service selects
        // this template (over the standard one) when the parcel's IsPriority flag is set.
        yield return Platform(
            NotificationTemplateKeys.ParcelReceivedPriority,
            "Priority parcel received",
            "A PRIORITY parcel ({provider}, {trackingNumber}) has been received for your unit and is held at {custodyLocation}. Please collect it promptly.",
            NotificationCategories.Parcel,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Sms | NotificationChannel.Email);

        // Parcel custody-SLA breach â†’ escalation notice to the recipient residents AND the community
        // admin (Req 31.2, Task 18.4). OPTIONAL "parcel" category (same opt-out rule as the other
        // parcel notices, Req 20.3). Req 64.2 says the SLA-exceeded notice targets push + email, so
        // the template declares exactly those two channels. Fills {trackingNumber}, {provider},
        // {custodyLocation}, {hoursInCustody}.
        yield return Platform(
            NotificationTemplateKeys.ParcelSlaExceeded,
            "Parcel awaiting collection",
            "A parcel ({provider}, {trackingNumber}) has been held at {custodyLocation} for {hoursInCustody} hours and has exceeded its collection SLA. Please arrange collection.",
            NotificationCategories.Parcel,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // Ticket assigned â†’ complaint-assigned push to the maintenance-staff assignee (Req 33.5, 64.3,
        // Task 20.2). OPTIONAL "complaint" category (a staff member can opt out, Req 20.3). Req 64.3
        // says a ticket-assigned notice targets the PUSH channel, so this template declares push only.
        // Fills {ticketTitle}, {category}, {priority}.
        yield return Platform(
            NotificationTemplateKeys.TicketAssigned,
            "Ticket assigned to you",
            "A {priority} {category} ticket has been assigned to you: {ticketTitle}. Please action it within its SLA.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // Ticket SLA breached â†’ escalation notice to the next responder (Req 33.3, Task 20.2). OPTIONAL
        // "complaint" category (same opt-out rule as the assigned notice, Req 20.3). The template
        // declares push + email so an overdue ticket reaches the escalation audience through more than
        // the in-app channel. Fills {ticketTitle}, {category}, {priority}, {hoursOverdue}.
        yield return Platform(
            NotificationTemplateKeys.TicketEscalated,
            "Ticket overdue â€” escalated",
            "A {priority} {category} ticket has breached its SLA by {hoursOverdue} hours and has been escalated to you: {ticketTitle}. Please respond.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // Ticket resolved â†’ complaint-resolved notice to the requesting resident (Req 34.3, Task 20.3).
        // OPTIONAL "complaint" category (same opt-out rule as the other ticket notices, Req 20.3). The
        // template declares push + email so the resident learns of the resolution (and can give
        // feedback / reopen) through more than the in-app channel. Fills {ticketTitle}, {category}.
        yield return Platform(
            NotificationTemplateKeys.TicketResolved,
            "Your ticket has been resolved",
            "Your {category} ticket has been resolved: {ticketTitle}. If the issue persists you can reopen it, and you're welcome to rate the service.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // Work order assigned â†’ work-order-assigned push to the maintenance-staff assignee (Req 36.3,
        // Task 21.2). OPTIONAL "complaint" category (a staff member can opt out, Req 20.3), following
        // the ticket-assigned notice; its template declares the PUSH channel. Fills {workOrderTitle},
        // {workOrderType}, {priority}.
        yield return Platform(
            NotificationTemplateKeys.WorkOrderAssigned,
            "Work order assigned to you",
            "A {priority} {workOrderType} work order has been assigned to you: {workOrderTitle}. Please action it.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // AMC contract approaching expiry â†’ expiry reminder to the responsible user (Req 37.3, Task
        // 21.3). OPTIONAL "complaint" category (the responsible user can opt out, Req 20.3), following
        // the other maintenance notices; its template declares push + email so the approaching expiry
        // reaches the responsible user through more than the in-app channel. Fills {assetName},
        // {vendorName}, {endDate}, {daysToExpiry}.
        yield return Platform(
            NotificationTemplateKeys.AmcExpiryReminder,
            "AMC contract expiring soon",
            "The AMC for {assetName} with {vendorName} expires on {endDate} ({daysToExpiry} days). Please arrange renewal.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // --- Facility bookings (Task 23.2, Req 39.4) ----------------------------------------------

        // A facility booking was created â†’ booking notice to the booking resident. The {status}
        // placeholder distinguishes the auto-confirmed case from "awaiting approval". OPTIONAL
        // "facility_booking" category (a resident can opt out, Req 20.3); push channel. Fills
        // {amenityName}, {bookingDate}, {status}.
        yield return Platform(
            NotificationTemplateKeys.BookingCreated,
            "Facility booking {status}",
            "Your booking of {amenityName} for {bookingDate} is {status}.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // A pending booking needs approval â†’ approval-needed notice to the amenity's approvers (Req
        // 39.3). OPTIONAL "facility_booking" category; push channel. Fills {amenityName}, {bookingDate}.
        yield return Platform(
            NotificationTemplateKeys.BookingApprovalNeeded,
            "Booking awaiting your approval",
            "A booking of {amenityName} for {bookingDate} is awaiting approval.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // A booking was confirmed/approved â†’ confirmation notice to the booking resident (Req 39.4).
        // OPTIONAL "facility_booking" category; push channel. Fills {amenityName}, {bookingDate}.
        yield return Platform(
            NotificationTemplateKeys.BookingConfirmed,
            "Facility booking confirmed",
            "Your booking of {amenityName} for {bookingDate} has been confirmed.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // A booking was cancelled â†’ cancellation notice to the booking resident (Req 39.4). OPTIONAL
        // "facility_booking" category; push channel. Fills {amenityName}, {bookingDate}.
        yield return Platform(
            NotificationTemplateKeys.BookingCancelled,
            "Facility booking cancelled",
            "Your booking of {amenityName} for {bookingDate} has been cancelled.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // --- Parking (Task 24.1, Req 40.4) --------------------------------------------------------

        // A parking violation was recorded against a slot assigned to the resident's unit â†’ notice to
        // that unit's residents (Req 40.4). OPTIONAL "parking" category (a resident can opt out, Req
        // 20.3); push channel. Fills {slotCode}, {violationType}, {description}.
        yield return Platform(
            NotificationTemplateKeys.ParkingViolationRecorded,
            "Parking violation recorded",
            "A parking violation ({violationType}) was recorded against slot {slotCode}: {description}.",
            NotificationCategories.Parking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // --- Guest parking, EV charging & resident events (Task 24.2, Req 41.1, 41.2, 41.5) -------

        // Guest parking assigned for a resident's approved visitor (Req 41.1) â†’ confirmation to the
        // requesting resident. OPTIONAL "parking" category (Req 20.3); push channel. Fills {slotCode},
        // {visitorName}, {validUntil}.
        yield return Platform(
            NotificationTemplateKeys.GuestParkingAssigned,
            "Guest parking assigned",
            "Guest parking slot {slotCode} has been assigned for your visitor {visitorName} until {validUntil}.",
            NotificationCategories.Parking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // EV-charging slot reserved for a resident (Req 41.2) â†’ confirmation to the reserving resident.
        // OPTIONAL "parking" category (Req 20.3); push channel. Fills {slotCode}, {reservedFrom},
        // {reservedUntil}.
        yield return Platform(
            NotificationTemplateKeys.EvReservationConfirmed,
            "EV charger reserved",
            "EV charging slot {slotCode} has been reserved for you from {reservedFrom} to {reservedUntil}.",
            NotificationCategories.Parking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // Resident registered for an event with available capacity (Req 41.5) â†’ confirmation to the
        // registering resident. OPTIONAL "event" category (Req 20.3); push channel. Fills {eventTitle},
        // {startsAt}.
        yield return Platform(
            NotificationTemplateKeys.ResidentEventRegistered,
            "Event registration confirmed",
            "You're registered for {eventTitle}, starting {startsAt}.",
            NotificationCategories.Event,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        yield return EmailLayoutTemplate();
    }

    private static NotificationTemplate EmailLayoutTemplate()
    {
        var html =
            "<!DOCTYPE html><html><body style=\"margin:0;padding:0;background:#f1f5f9;\">" +
            "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f1f5f9;padding:24px 0;\"><tr><td align=\"center\">" +
            "<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:600px;max-width:92%;background:#ffffff;border-radius:16px;overflow:hidden;font-family:Arial,Helvetica,sans-serif;\">" +
            "<tr><td style=\"background:#0b261d;padding:20px 28px;\" align=\"left\">" +
            "<img src=\"{logoUrl}\" width=\"40\" height=\"40\" alt=\"AanganOS\" style=\"vertical-align:middle;border-radius:8px;\" />" +
            "<span style=\"color:#ffffff;font-size:20px;font-weight:bold;margin-left:10px;vertical-align:middle;\">Aangan<span style=\"color:#8ef0c0;\">OS</span></span>" +
            "</td></tr>" +
            "<tr><td style=\"padding:28px;color:#0f172a;font-size:15px;line-height:1.6;\">{content}</td></tr>" +
            "<tr><td style=\"padding:18px 28px;background:#f8fafc;color:#64748b;font-size:12px;line-height:1.5;border-top:1px solid #e2e8f0;\">" +
            "You are receiving this email from {communityName}. This is an automated message from AanganOS." +
            "</td></tr>" +
            "</table></td></tr></table></body></html>";

        return new NotificationTemplate
        {
            Id = DeterministicGuid($"notif-template|{NotificationTemplateKeys.EmailLayout}"),
            Key = NotificationTemplateKeys.EmailLayout,
            Scope = ConfigScope.Platform,
            CommunityId = null,
            Category = NotificationCategories.Account,
            CategoryKind = NotificationCategoryKind.Mandatory,
            TitleTemplate = "Email layout",
            BodyTemplate = "Branded email shell with a {content} slot.",
            EmailHtmlTemplate = html,
            Channels = NotificationChannel.Email,
            IsActive = true,
            CreatedAtUtc = SeedTimestamp,
            CreatedBy = "seed",
            RowVersion = [],
        };
    }

    private static NotificationTemplate Platform(
        string key,
        string title,
        string body,
        string category = NotificationCategories.Account,
        NotificationCategoryKind categoryKind = NotificationCategoryKind.Mandatory,
        NotificationChannel channels = NotificationChannel.Push) => new()
    {
        // Deterministic id derived from the key so re-seeding is stable and never duplicates.
        Id = DeterministicGuid($"notif-template|{key}"),
        Key = key,
        Scope = ConfigScope.Platform,
        CommunityId = null,
        // Verification/approval are account notices â†’ mandatory (Req 20.4); the visitor-request
        // notice is an optional "visitor" category (Req 20.3) passed in by the caller.
        Category = category,
        CategoryKind = categoryKind,
        TitleTemplate = title,
        BodyTemplate = body,
        Channels = channels,
        IsActive = true,
        // Audit columns are non-nullable; give the seed a fixed, deterministic stamp.
        CreatedAtUtc = SeedTimestamp,
        CreatedBy = "seed",
        RowVersion = [],
    };

    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }
}
