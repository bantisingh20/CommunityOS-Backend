namespace App.Application.Config;

/// <summary>
/// Stable, well-known configuration keys the platform itself resolves through Config_Service, kept
/// in one place instead of as magic strings scattered around (steering: no hardcoding). These name
/// settings whose <i>values</i> live in config/seed data and are editable per community — the
/// constant is only the key, never the value.
/// </summary>
public static class ConfigKeys
{
    /// <summary>Per-request default page size override for list endpoints (falls back to PaginationOptions).</summary>
    public const string PaginationDefaultPageSize = "pagination.defaultPageSize";

    /// <summary>Per-request maximum page size override for list endpoints (falls back to PaginationOptions).</summary>
    public const string PaginationMaxPageSize = "pagination.maxPageSize";

    /// <summary>
    /// Minimum <c>Incident_Severity</c> rank (the severity list's SortOrder) at or above which a
    /// reported incident AUTO-ESCALATES to the configured responders (Req 27.2). Resolved per
    /// community via Config_Service so a community tunes the threshold without a code change
    /// (steering: no hardcoding). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string IncidentEscalationThreshold = "gate.incident.escalationThreshold";

    /// <summary>
    /// Comma-separated list of <c>Parcel_Category</c> codes treated as PRIORITY (medicine /
    /// perishable / urgent, Req 29.3). A parcel registered under one of these categories is flagged
    /// priority so the parcel-received notification (Task 18.2) gets priority handling. Resolved per
    /// community via Config_Service so a community tunes which categories are urgent without a code
    /// change (steering: no hardcoding). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string ParcelPriorityCategories = "parcel.priorityCategories";

    /// <summary>
    /// Default verification-method code a parcel authorization uses when the resident does not pick
    /// one (otp / qr / id / combination, Req 30.2). Resolved per community via Config_Service so a
    /// community sets its own default handover verification without a code change (steering: no
    /// hardcoding). Falls back to a seeded platform default (otp) when unset.
    /// </summary>
    public const string ParcelHandoverDefaultVerificationMethod = "parcel.handover.defaultVerificationMethod";

    /// <summary>
    /// Number of digits in the OTP shared with an authorized parcel collector (Req 30.2). Resolved
    /// per community via Config_Service so OTP strength is tunable without a code change (steering:
    /// no hardcoding). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string ParcelHandoverOtpLength = "parcel.handover.otpLength";

    /// <summary>
    /// How many hours a parcel authorization (and its OTP/QR) stays valid from creation (Req 30.1).
    /// Resolved per community via Config_Service so authorization lifetime is tunable without a code
    /// change (steering: no hardcoding). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string ParcelHandoverAuthorizationValidityHours = "parcel.handover.authorizationValidityHours";

    /// <summary>
    /// How many hours a parcel may sit in custody before it breaches its Parcel_SLA and the custody
    /// SLA sweep escalates it (Req 31.2). Measured from when the parcel was taken into custody.
    /// Resolved per community via Config_Service so a community tunes its custody SLA without a code
    /// change (steering: no hardcoding). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string ParcelCustodySlaHours = "parcel.custody.slaHours";

    /// <summary>
    /// Default <c>Ticket_Priority</c> code a helpdesk ticket takes when the resident does not pick
    /// one (Req 32.1). Resolved per community via Config_Service so a community sets its own default
    /// ticket priority without a code change (steering: no hardcoding). Falls back to a seeded
    /// platform default (medium) when unset.
    /// </summary>
    public const string TicketDefaultPriority = "ticket.defaultPriority";

    /// <summary>
    /// Prefix for the per-priority ticket SLA duration keys (Req 33.2). The resolution-time SLA for a
    /// ticket of priority <c>p</c> is read from <c>ticket.sla.hours.{p}</c> (e.g.
    /// <c>ticket.sla.hours.urgent</c>) via <see cref="TicketSlaHoursKey"/>, resolved per community
    /// (platform fallback) through Config_Service so each community tunes its per-priority SLA without
    /// a code change (steering: no hardcoding) — never a hardcoded hours table. Falls back to a seeded
    /// platform default per priority when unset.
    /// </summary>
    public const string TicketSlaHoursPrefix = "ticket.sla.hours.";

    /// <summary>
    /// Build the per-priority ticket SLA duration key for <paramref name="priority"/> (Req 33.2):
    /// <c>ticket.sla.hours.{priority}</c>. The priority is a <c>Ticket_Priority</c> code, so the key
    /// stays aligned with the editable priority list — adding a new priority only needs a new config
    /// value, not a code change (steering: no hardcoding).
    /// </summary>
    public static string TicketSlaHoursKey(string priority) => $"{TicketSlaHoursPrefix}{priority}";

    /// <summary>
    /// Default <c>User</c> id a helpdesk ticket is auto-assigned to when NO <c>Ticket_Assignment_Rule</c>
    /// matches its category (Req 33.1). Resolved per community via Config_Service so a community sets
    /// its own fallback assignee (e.g. the facility manager) without a code change (steering: no
    /// hardcoding). When unset AND no rule matches, the ticket is left unassigned (status stays
    /// <c>new</c>) — a deliberate, documented choice so a mis-configured community never mis-routes a
    /// ticket to an arbitrary staff member.
    /// </summary>
    public const string TicketDefaultAssigneeUserId = "ticket.defaultAssigneeUserId";

    /// <summary>
    /// Minimum allowed <c>Ticket.Rating</c> on the configurable feedback scale (Req 34.4). Resolved
    /// per community via Config_Service so a community sets its own rating scale without a code change
    /// (steering: no hardcoding — never a hardcoded min). Falls back to a seeded platform default (1)
    /// when unset. A submitted rating below this bound is a validation error.
    /// </summary>
    public const string TicketFeedbackMinRating = "ticket.feedback.minRating";

    /// <summary>
    /// Maximum allowed <c>Ticket.Rating</c> on the configurable feedback scale (Req 34.4). Resolved
    /// per community via Config_Service so a community sets its own rating scale (e.g. 1..5, 1..10)
    /// without a code change (steering: no hardcoding — never a hardcoded max). Falls back to a seeded
    /// platform default (5) when unset. A submitted rating above this bound is a validation error.
    /// </summary>
    public const string TicketFeedbackMaxRating = "ticket.feedback.maxRating";

    // --- Maintenance work orders (Phase 6, Task 21.2, Req 36.1, 36.2) ------------------------------

    /// <summary>
    /// Prefix for the per-frequency preventive-plan interval keys (Req 36.1). The spacing in days for
    /// a <c>Maintenance_Plan_Frequency</c> code <c>f</c> is read from
    /// <c>maintenance.plan.intervalDays.{f}</c> (e.g. <c>maintenance.plan.intervalDays.monthly</c>)
    /// via <see cref="MaintenancePlanIntervalDaysKey"/>, resolved per community (platform fallback)
    /// through Config_Service so each community tunes its per-frequency spacing without a code change
    /// (steering: no hardcoding) — never a hardcoded day table. Falls back to a seeded platform
    /// default per frequency when unset. A <c>custom</c> frequency ignores this and uses the plan's
    /// own supplied interval.
    /// </summary>
    public const string MaintenancePlanIntervalDaysPrefix = "maintenance.plan.intervalDays.";

    /// <summary>
    /// Build the per-frequency preventive-plan interval key for <paramref name="frequency"/>
    /// (Req 36.1): <c>maintenance.plan.intervalDays.{frequency}</c>. The frequency is a
    /// <c>Maintenance_Plan_Frequency</c> code, so the key stays aligned with the editable frequency
    /// list — adding a new frequency only needs a new config value, not a code change (steering: no
    /// hardcoding).
    /// </summary>
    public static string MaintenancePlanIntervalDaysKey(string frequency) => $"{MaintenancePlanIntervalDaysPrefix}{frequency}";

    /// <summary>
    /// Default <c>Work_Order_Priority</c> code a corrective work order takes when the raiser does not
    /// pick one (Req 36.2). Resolved per community via Config_Service so a community sets its own
    /// default work-order priority without a code change (steering: no hardcoding). Falls back to a
    /// seeded platform default (medium) when unset.
    /// </summary>
    public const string WorkOrderDefaultPriority = "maintenance.workOrder.defaultPriority";

    // --- Maintenance AMC contracts (Phase 6, Task 21.3, Req 37.3) ----------------------------------

    /// <summary>
    /// How many days BEFORE an AMC contract's end date it enters the expiry-reminder window (Req 37.3).
    /// While <c>now &gt;= EndDate - this</c> (and the contract is still active and not yet reminded for
    /// this end date), the background AMC sweep notifies the responsible user. Resolved per community
    /// via Config_Service so a community tunes its reminder lead time without a code change (steering:
    /// no hardcoding — never a hardcoded day count). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string AmcExpiryReminderDays = "maintenance.amc.expiryReminderDays";

    // --- Amenities & bookings (Phase 6, Task 23.1, Req 38.1-38.4) ----------------------------------

    /// <summary>
    /// Default advance-booking window in days a new amenity's <c>BookingRule</c> takes when the
    /// Facility Manager does not set one explicitly (Req 38.3). Resolved per community via
    /// Config_Service so a community sets its own default advance window without a code change
    /// (steering: no hardcoding). Falls back to a seeded platform default when unset. A per-amenity
    /// rule value overrides this once configured.
    /// </summary>
    public const string AmenityDefaultAdvanceBookingWindowDays = "amenity.booking.defaultAdvanceWindowDays";

    /// <summary>
    /// Default maximum bookings a single resident may hold for an amenity within the default period
    /// when the Facility Manager does not set a per-amenity cap (Req 38.3). Resolved per community via
    /// Config_Service so a community sets its own default per-resident cap without a code change
    /// (steering: no hardcoding). Falls back to a seeded platform default when unset. A per-amenity
    /// rule value overrides this once configured.
    /// </summary>
    public const string AmenityDefaultMaxBookingsPerResident = "amenity.booking.defaultMaxBookingsPerResident";

    // --- Guest parking, EV charging & resident events (Phase 6, Task 24.2, Req 41.1-41.5) ----------

    /// <summary>
    /// Maximum duration (hours) a resident-requested GUEST parking assignment may span (Req 41.1).
    /// The guest assignment's end is bounded by this from its start. Resolved per community via
    /// Config_Service so a community tunes its guest-parking window without a code change (steering:
    /// no hardcoding). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string GuestParkingMaxDurationHours = "parking.guest.maxDurationHours";

    /// <summary>
    /// Maximum number of CONCURRENT (active) guest parking assignments a single unit may hold at once
    /// (Req 41.1). A request beyond this is rejected. Resolved per community via Config_Service so a
    /// community tunes the per-unit guest cap without a code change (steering: no hardcoding). Falls
    /// back to a seeded platform default when unset.
    /// </summary>
    public const string GuestParkingMaxConcurrentPerUnit = "parking.guest.maxConcurrentPerUnit";

    /// <summary>
    /// Maximum duration (hours) a single EV-charging reservation window may span (Req 41.2). The
    /// reserved window (<c>ReservedUntilUtc - ReservedFromUtc</c>) must not exceed this. Resolved per
    /// community via Config_Service so a community tunes its charging-session window without a code
    /// change (steering: no hardcoding). Falls back to a seeded platform default when unset.
    /// </summary>
    public const string EvReservationMaxDurationHours = "parking.ev.maxReservationHours";

    /// <summary>
    /// Maximum number of CONCURRENT (active) EV reservations a single unit may hold at once (Req
    /// 41.2). A reservation beyond this is rejected. Resolved per community via Config_Service so a
    /// community tunes the per-unit EV cap without a code change (steering: no hardcoding). Falls back
    /// to a seeded platform default when unset.
    /// </summary>
    public const string EvReservationMaxConcurrentPerUnit = "parking.ev.maxConcurrentPerUnit";

    public const string MaintenanceBillReminderDaysBeforeDue = "maintenance.bill.reminderDaysBeforeDue";
}

/// <summary>
/// Well-known master-data list keys the platform seeds out of the box. The set is open: later
/// modules add their own list simply by seeding rows under a new key, so these constants are a
/// convenience for the lists Phase 1/2 reference directly, not a closed universe.
/// </summary>
public static class MasterDataKeys
{
    /// <summary>Unit classifications the Phase 2 Unit entity validates its type against (Req 8.4).</summary>
    public const string UnitType = "Unit_Type";

    /// <summary>Unit occupancy/availability statuses the Phase 2 Unit entity validates against (Req 8.4).</summary>
    public const string UnitStatus = "Unit_Status";

    /// <summary>
    /// Configurable community types a Community is classified as (Req 12.2): apartment society,
    /// gated community, villa/bungalow, townhouse, mixed. Editable per community, not a frozen enum.
    /// </summary>
    public const string CommunityType = "Community_Type";

    /// <summary>
    /// Configurable announcement categories the notice board classifies a notice by (general / event
    /// / maintenance / emergency). Editable per community, validated at runtime, not a frozen enum.
    /// </summary>
    public const string AnnouncementCategory = "Announcement_Category";

    /// <summary>
    /// Configurable property-hierarchy levels (Req 13.1, 13.2): Phase, Building/Tower, Block, Zone,
    /// Floor. Levels are optional and editable — a HierarchyNode's level validates against this list.
    /// </summary>
    public const string HierarchyLevel = "Hierarchy_Level";

    /// <summary>
    /// Configurable resident classifications a Resident is typed as (Req 15.1): owner, co_owner,
    /// tenant, family_member. Editable per community, validated at runtime, not a frozen enum.
    /// </summary>
    public const string ResidentType = "Resident_Type";

    /// <summary>
    /// Configurable resident verification statuses (Req 15.1, 17.x): pending, verified, rejected.
    /// Create defaults to pending; the Task 12.3 workflow drives transitions against this list.
    /// </summary>
    public const string ResidentVerificationStatus = "Resident_Verification_Status";

    /// <summary>
    /// Configurable vehicle classifications a Vehicle is typed as (Req 19.1): two_wheeler,
    /// four_wheeler, bicycle, commercial, other. Validated at runtime, not a frozen enum.
    /// </summary>
    public const string VehicleType = "Vehicle_Type";

    /// <summary>
    /// Configurable pet classifications a Pet is typed as (Req 19.3): dog, cat, bird, other.
    /// Validated at runtime, not a frozen enum.
    /// </summary>
    public const string PetType = "Pet_Type";

    /// <summary>
    /// Configurable notification categories a resident opts into (Req 20.1): the same vocabulary the
    /// Task 7.1 notification templates classify against (account, resident verification, parcel,
    /// complaint, invoice, facility booking, emergency, document expiry, ...). Editable per community,
    /// validated at runtime, not a frozen enum. The <c>account</c> category is seeded here too so the
    /// list is complete, but it is a mandatory category — the Notification_Service always sends it
    /// regardless of a resident's opt-in (Req 20.4), so opting out of it has no effect.
    /// </summary>
    public const string NotificationCategory = "Notification_Category";

    /// <summary>
    /// Configurable visitor categories a Visitor is classified as (Req 24.1, 25.1): guest, cab,
    /// delivery, service_provider, contractor. Validated at runtime against the editable list, not a
    /// frozen enum. The full category behaviour (recurring/entry-exit) is Task 16.2; Task 16.1
    /// defines + validates the field.
    /// </summary>
    public const string VisitorCategory = "Visitor_Category";

    /// <summary>
    /// Configurable ID-proof types a walk-in visitor's identity document is classified as
    /// (aadhaar, pan, driving_license, voter_id, passport). Captured for future reference and
    /// validated at runtime against the editable list, not a frozen enum (steering: no hardcoding).
    /// </summary>
    public const string IdProofType = "ID_Proof_Type";

    /// <summary>
    /// Configurable visit-pass statuses (Req 23.4): active, used, expired, revoked. The create-time
    /// default (active) and the verification state machine resolve against this editable list rather
    /// than a frozen enum (steering: no hardcoding).
    /// </summary>
    public const string VisitPassStatus = "Visit_Pass_Status";

    /// <summary>
    /// Configurable visitor (walk-in) statuses (Req 24.3, 24.4): pending, approved, rejected,
    /// entered, exited. Create defaults to pending; the resident approval workflow drives transitions
    /// against this editable list, not a frozen enum.
    /// </summary>
    public const string VisitorStatus = "Visitor_Status";

    /// <summary>
    /// Configurable recurring-visitor statuses (Req 25.2): active, revoked. Create defaults to
    /// active; a resident revoke drives the transition against this editable list, not a frozen enum.
    /// </summary>
    public const string RecurringVisitorStatus = "Recurring_Visitor_Status";

    // --- Gate security operations (Task 16.3, Req 26.x, 27.x) ---------------------------------------

    /// <summary>
    /// Configurable watchlist subject types (Req 26.1): person, vehicle. Validated at runtime against
    /// the editable list, not a frozen enum.
    /// </summary>
    public const string WatchlistSubjectType = "Watchlist_Subject_Type";

    /// <summary>
    /// Configurable watchlist severities (Req 26.1): low, medium, high, critical. Editable list.
    /// </summary>
    public const string WatchlistSeverity = "Watchlist_Severity";

    /// <summary>
    /// Configurable watchlist statuses (Req 26.5): active, cleared. Create defaults to active (flags
    /// at the gate); a clear sets cleared. Editable list, not a frozen enum.
    /// </summary>
    public const string WatchlistStatus = "Watchlist_Status";

    /// <summary>
    /// Configurable guard-shift statuses (Req 26.4): scheduled, active, completed, cancelled. Roster
    /// create defaults to scheduled. Editable list, not a frozen enum.
    /// </summary>
    public const string GuardShiftStatus = "Guard_Shift_Status";

    /// <summary>
    /// Configurable incident categories (Req 27.1): trespass, theft, vandalism, medical, fire,
    /// dispute, other. Validated at runtime against the editable list, not a frozen enum.
    /// </summary>
    public const string IncidentCategory = "Incident_Category";

    /// <summary>
    /// Configurable incident severities (Req 27.1): low, medium, high, critical. The severity RANK is
    /// the list's SortOrder, compared against <see cref="ConfigKeys.IncidentEscalationThreshold"/> to
    /// decide auto-escalation (Req 27.2). Editable list, not a frozen enum.
    /// </summary>
    public const string IncidentSeverity = "Incident_Severity";

    /// <summary>
    /// Configurable incident statuses (Req 27.1): open, acknowledged, escalated, resolved. Report
    /// defaults to open. Editable list, not a frozen enum.
    /// </summary>
    public const string IncidentStatus = "Incident_Status";

    /// <summary>
    /// Configurable incident escalation states (Req 27.2): none, escalated. Report defaults to none;
    /// severity meeting the threshold sets escalated. Editable list, not a frozen enum.
    /// </summary>
    public const string IncidentEscalationState = "Incident_Escalation_State";

    /// <summary>
    /// Configurable SOS-alert statuses (Req 27.4): active, acknowledged, resolved. Raise defaults to
    /// active; stays active until Edit acknowledges + resolves. Editable list, not a frozen enum.
    /// </summary>
    public const string SosAlertStatus = "Sos_Alert_Status";

    /// <summary>
    /// Configurable access-integration actions (Req 27.5): open_barrier, close_barrier, cctv_snapshot,
    /// access_event. Validated at runtime against the editable list, not a frozen enum.
    /// </summary>
    public const string AccessIntegrationAction = "Access_Integration_Action";

    /// <summary>
    /// Configurable access-integration event statuses (Req 27.5): triggered, succeeded, failed.
    /// Editable list, not a frozen enum.
    /// </summary>
    public const string AccessIntegrationStatus = "Access_Integration_Status";

    // --- Parcel, Delivery & Custody (Phase 4, Task 18.x, Req 28.x–31.x) ----------------------------

    /// <summary>
    /// Configurable parcel statuses (Req 28.2): expected, received, owner_not_available, in_custody,
    /// ready_for_pickup, handed_over, returned, lost_damaged. Registration defaults to received; the
    /// Task 18.1 state machine drives transitions against this editable list, not a frozen enum.
    /// </summary>
    public const string ParcelStatus = "Parcel_Status";

    /// <summary>
    /// Configurable parcel categories (Req 28.1): standard, medicine, perishable, urgent, document.
    /// Validated at runtime against the editable list, not a frozen enum. Which categories are
    /// treated as priority is itself configurable (<see cref="ConfigKeys.ParcelPriorityCategories"/>,
    /// Req 29.3).
    /// </summary>
    public const string ParcelCategory = "Parcel_Category";

    /// <summary>
    /// Configurable recorded package conditions a parcel is received in (Req 28.1): good, damaged,
    /// opened, wet. Validated at runtime against the editable list, not a frozen enum.
    /// </summary>
    public const string ParcelCondition = "Parcel_Condition";

    /// <summary>
    /// Configurable ALLOWED parcel status transitions (Req 28.3). Each entry's code is a
    /// <c>from-&gt;to</c> pair of <see cref="ParcelStatus"/> codes (e.g. <c>received-&gt;in_custody</c>);
    /// a transition is permitted only when its pair is present (and active) in this list for the
    /// community. Driven by editable master data so a community tunes its lifecycle without a code
    /// change (steering: no hardcoding) — a transition not in the list is rejected with a validation
    /// error.
    /// </summary>
    public const string ParcelStatusTransition = "Parcel_Status_Transition";

    /// <summary>
    /// Configurable collector verification methods a parcel handover may require (Req 30.2): otp, qr,
    /// id, combination. Validated at runtime against the editable list, not a frozen enum — a
    /// community can add/retire a method without a code change (steering: no hardcoding). The handover
    /// enforces the method the authorization names.
    /// </summary>
    public const string ParcelVerificationMethod = "Parcel_Verification_Method";

    // --- Helpdesk tickets (Phase 5, Task 20.x, Req 32.x–34.x) --------------------------------------

    /// <summary>
    /// Configurable ticket categories a helpdesk Ticket is classified as (Req 32.1, 32.2): plumbing,
    /// electrical, housekeeping, security, common_area, other. Validated at runtime against the
    /// editable list, not a frozen enum — an admin adds/retires a category without a code change
    /// (steering: no hardcoding).
    /// </summary>
    public const string TicketCategory = "Ticket_Category";

    /// <summary>
    /// Configurable ticket subcategories a helpdesk Ticket may be refined by (Req 32.1, 32.2).
    /// Optional on a ticket (not every category needs one). Validated at runtime against the editable
    /// list, not a frozen enum.
    /// </summary>
    public const string TicketSubcategory = "Ticket_Subcategory";

    /// <summary>
    /// Configurable ticket priorities a helpdesk Ticket is raised at (Req 32.1, 32.2): low, medium,
    /// high, urgent. The SLA timer by priority (Task 20.2) resolves against this editable list, not a
    /// frozen enum.
    /// </summary>
    public const string TicketPriority = "Ticket_Priority";

    /// <summary>
    /// Configurable ticket statuses (Req 34.1): new, assigned, in_progress, waiting, resolved,
    /// closed, reopened. Creation defaults to new; the Task 20.3 state machine drives transitions
    /// against this editable list, not a frozen enum.
    /// </summary>
    public const string TicketStatus = "Ticket_Status";

    /// <summary>
    /// Configurable ALLOWED ticket status transitions (Task 20.2, Req 34.1). Each entry's code is a
    /// <c>from-&gt;to</c> pair of <see cref="TicketStatus"/> codes (e.g. <c>new-&gt;assigned</c>); a
    /// transition is permitted only when its pair is present (and active) in this list for the
    /// community. Driven by editable master data so a community tunes its ticket lifecycle without a
    /// code change (steering: no hardcoding) — a transition not in the list is rejected with a
    /// validation error. Mirrors <see cref="ParcelStatusTransition"/>. Assignment moves a ticket
    /// <c>new-&gt;assigned</c> through this allow-list.
    /// </summary>
    public const string TicketStatusTransition = "Ticket_Status_Transition";

    /// <summary>
    /// Configurable ticket auto-assignment rules by category (Task 20.2, Req 33.1). Each entry's code
    /// is a <c>category-&gt;userId</c> pair — the <c>Ticket_Category</c> code mapped to the
    /// maintenance-staff <c>User</c> id that category routes to. On creation the service looks up the
    /// rule for the ticket's category and (when the mapped user is still a valid staff assignee)
    /// assigns the ticket to it. Driven by editable master data (per community) so routing is tuned
    /// without a code change (steering: no hardcoding) — never a hardcoded category→assignee table.
    /// No seeded platform defaults: assignment rules are inherently per-community (they name a
    /// community's own staff users), so a community adds its own entries; absent any rule the ticket
    /// falls back to <see cref="ConfigKeys.TicketDefaultAssigneeUserId"/> or stays unassigned.
    /// </summary>
    public const string TicketAssignmentRule = "Ticket_Assignment_Rule";

    // --- Maintenance assets (Phase 6, Task 21.x, Req 35.x) -----------------------------------------

    /// <summary>
    /// Configurable asset lifecycle statuses a maintenance <c>Asset</c> is tracked against (Req 35.1):
    /// active, under_maintenance, inactive, retired, disposed. Registration defaults to active.
    /// Validated at runtime against the editable list, not a frozen enum — an admin adds/retires a
    /// status without a code change (steering: no hardcoding). 21.1 validates the status as a member
    /// of this list; a transition allow-list is intentionally not introduced for the asset lifecycle.
    /// </summary>
    public const string AssetStatus = "Asset_Status";

    /// <summary>
    /// Configurable asset types a maintenance <c>Asset</c> is classified as (Req 35.1): lift, pump,
    /// dg_set, hvac, fire_safety, electrical, plumbing, other. Validated at runtime against the
    /// editable list, not a frozen enum.
    /// </summary>
    public const string AssetType = "Asset_Type";

    /// <summary>
    /// Configurable asset categories a maintenance <c>Asset</c> may be grouped by (Req 35.1):
    /// common_area, utility, safety, amenity, other. Optional on an asset. Validated at runtime
    /// against the editable list, not a frozen enum.
    /// </summary>
    public const string AssetCategory = "Asset_Category";

    // --- Maintenance work orders (Phase 6, Task 21.2, Req 36.1-36.5) -------------------------------

    /// <summary>
    /// Configurable preventive-maintenance plan frequencies a <c>MaintenancePlan</c> schedules against
    /// (Req 36.1): daily, weekly, monthly, quarterly, half_yearly, yearly, custom. Validated at
    /// runtime against the editable list, not a frozen enum. The concrete spacing in days for each
    /// frequency is itself configurable (<see cref="ConfigKeys.MaintenancePlanIntervalDaysPrefix"/>);
    /// <c>custom</c> carries the plan's own supplied interval.
    /// </summary>
    public const string MaintenancePlanFrequency = "Maintenance_Plan_Frequency";

    /// <summary>
    /// Configurable work-order types a <c>WorkOrder</c> is classified as (Req 36.1, 36.2): preventive
    /// (generated from a plan), corrective (raised directly). Validated at runtime against the editable
    /// list, not a frozen enum.
    /// </summary>
    public const string WorkOrderType = "Work_Order_Type";

    /// <summary>
    /// Configurable work-order priorities a <c>WorkOrder</c> is raised at (Req 36.2): low, medium,
    /// high, urgent. Validated at runtime against the editable list, not a frozen enum.
    /// </summary>
    public const string WorkOrderPriority = "Work_Order_Priority";

    /// <summary>
    /// Configurable work-order statuses (Req 36.1-36.5): open, assigned, in_progress, on_hold,
    /// completed, cancelled. Creation/generation defaults to open; the status state machine drives
    /// transitions against this editable list, not a frozen enum.
    /// </summary>
    public const string WorkOrderStatus = "Work_Order_Status";

    /// <summary>
    /// Configurable ALLOWED work-order status transitions (Req 36.1-36.5). Each entry's code is a
    /// <c>from-&gt;to</c> pair of <see cref="WorkOrderStatus"/> codes (e.g. <c>open-&gt;assigned</c>);
    /// a transition is permitted only when its pair is present (and active) in this list for the
    /// community. Driven by editable master data so a community tunes its work-order lifecycle without
    /// a code change (steering: no hardcoding) — a transition not in the list is rejected with a
    /// validation error. Mirrors <see cref="TicketStatusTransition"/> / <see cref="ParcelStatusTransition"/>.
    /// Assignment moves a work order <c>open-&gt;assigned</c> (or <c>*-&gt;assigned</c> on re-assign)
    /// through this allow-list.
    /// </summary>
    public const string WorkOrderStatusTransition = "Work_Order_Status_Transition";

    // --- Maintenance inspections / AMC / meters (Phase 6, Task 21.3, Req 37.1-37.4) ----------------

    /// <summary>
    /// Configurable inspection result codes an <c>InspectionChecklist</c> and each of its items are
    /// recorded against (Req 37.1): pass, fail, pass_with_observations, not_applicable. Validated at
    /// runtime against the editable list, not a frozen enum.
    /// </summary>
    public const string InspectionResult = "Inspection_Result";

    /// <summary>
    /// Configurable AMC contract statuses an <c>AmcContract</c> is tracked against (Req 37.2): active,
    /// expired, cancelled. Creation defaults to active. Validated at runtime against the editable
    /// list, not a frozen enum.
    /// </summary>
    public const string AmcContractStatus = "Amc_Contract_Status";

    /// <summary>
    /// Configurable equipment meter types a <c>MeterReading</c> is recorded for (Req 37.4):
    /// running_hours, energy_kwh, flow, cycles. Identifies which counter on an asset a reading is for.
    /// Validated at runtime against the editable list, not a frozen enum.
    /// </summary>
    public const string MeterType = "Meter_Type";

    /// <summary>
    /// Configurable measurement units a <c>MeterReading</c> is recorded in (Req 37.4): hours, kwh,
    /// litres, cycles. Validated at runtime against the editable list, not a frozen enum.
    /// </summary>
    public const string MeterUnit = "Meter_Unit";

    // --- Amenities & bookings (Phase 6, Task 23.1, Req 38.1) ---------------------------------------

    /// <summary>
    /// Configurable amenity types a bookable <c>Amenity</c> is classified as (Req 38.1): clubhouse,
    /// gym, pool, hall, court, garden, party_area, custom. Validated at runtime against the editable
    /// list, not a frozen enum — a community adds/retires a type without a code change (steering: no
    /// hardcoding).
    /// </summary>
    public const string AmenityType = "Amenity_Type";

    /// <summary>
    /// Configurable amenity lifecycle statuses an <c>Amenity</c> is tracked against (Req 38.1):
    /// active, inactive, under_maintenance. Creation defaults to active. Validated at runtime against
    /// the editable list, not a frozen enum. 23.1 validates the status as a member of this list; a
    /// transition allow-list is intentionally not introduced for the amenity lifecycle.
    /// </summary>
    public const string AmenityStatus = "Amenity_Status";

    // --- Booking lifecycle (Phase 6, Task 23.2, Req 39.1-39.5) -------------------------------------

    /// <summary>
    /// Configurable booking lifecycle statuses a <c>Booking</c> is tracked against (Req 39.1, 39.3):
    /// requested, confirmed, cancelled, completed. Creation defaults to requested (approval-required
    /// amenity) or confirmed (auto-confirm). Validated at runtime against the editable list, not a
    /// frozen enum — a community adds/retires a status without a code change (steering: no hardcoding).
    /// </summary>
    public const string BookingStatus = "Booking_Status";

    /// <summary>
    /// Configurable ALLOWED booking status transitions (Req 39.3, 39.4). Each entry's code is a
    /// <c>from-&gt;to</c> pair of <see cref="BookingStatus"/> codes (e.g. <c>requested-&gt;confirmed</c>);
    /// a transition is permitted only when its pair is present (and active) in this list for the
    /// community. Driven by editable master data so a community tunes its booking lifecycle without a
    /// code change (steering: no hardcoding) — a transition not in the list is rejected with a
    /// validation error. Mirrors <see cref="TicketStatusTransition"/> / <see cref="ParcelStatusTransition"/>.
    /// Approval moves a booking <c>requested-&gt;confirmed</c>; cancellation moves <c>requested-&gt;cancelled</c>
    /// or <c>confirmed-&gt;cancelled</c> through this allow-list.
    /// </summary>
    public const string BookingStatusTransition = "Booking_Status_Transition";

    // --- Parking (Phase 6, Task 24.1, Req 40.1-40.4) -----------------------------------------------

    /// <summary>
    /// Configurable parking-zone types a <c>ParkingZone</c> is classified as (Req 40.1): surface,
    /// basement, podium, visitor, ev. Validated at runtime against the editable list, not a frozen
    /// enum — a community adds/retires a type without a code change (steering: no hardcoding).
    /// </summary>
    public const string ParkingZoneType = "Parking_Zone_Type";

    /// <summary>
    /// Configurable parking-slot types a <c>ParkingSlot</c> is classified as (Req 40.1): open,
    /// covered, visitor, ev, accessible. Validated at runtime against the editable list, not a frozen
    /// enum. The visitor/ev types are what the Task 24.2 guest-parking / EV-reservation flows key off.
    /// </summary>
    public const string ParkingSlotType = "Parking_Slot_Type";

    /// <summary>
    /// Configurable parking-assignment types a <c>ParkingAssignment</c> is classified as (Req 40.2):
    /// permanent, temporary, visitor. Validated at runtime against the editable list, not a frozen
    /// enum.
    /// </summary>
    public const string ParkingAssignmentType = "Parking_Assignment_Type";

    /// <summary>
    /// Configurable parking-violation types a <c>ParkingViolation</c> is classified as (Req 40.4):
    /// wrong_slot, overstay, blocking, no_permit, unauthorized. Validated at runtime against the
    /// editable list, not a frozen enum.
    /// </summary>
    public const string ParkingViolationType = "Parking_Violation_Type";

    /// <summary>
    /// Configurable parking-violation lifecycle statuses a <c>ParkingViolation</c> is tracked against
    /// (Req 40.4): open, resolved, waived. Creation defaults to open. Validated at runtime against the
    /// editable list, not a frozen enum.
    /// </summary>
    public const string ParkingViolationStatus = "Parking_Violation_Status";

    // --- Guest parking, EV charging & resident events (Phase 6, Task 24.2, Req 41.1-41.5) ----------

    /// <summary>
    /// Configurable EV-reservation lifecycle statuses an <c>EvReservation</c> is tracked against (Req
    /// 41.2): reserved, charging, completed, cancelled. Creation defaults to reserved. Validated at
    /// runtime against the editable list, not a frozen enum — a community adds/retires a status
    /// without a code change (steering: no hardcoding).
    /// </summary>
    public const string EvReservationStatus = "Ev_Reservation_Status";

    /// <summary>
    /// Configurable resident-event lifecycle statuses a <c>ResidentEvent</c> is tracked against (Req
    /// 41.4): published, cancelled, completed. Creation defaults to published. Validated at runtime
    /// against the editable list, not a frozen enum.
    /// </summary>
    public const string ResidentEventStatus = "Resident_Event_Status";

    public const string MaintenanceBillStatus = "Maintenance_Bill_Status";

    public const string MaintenancePaymentMethod = "Maintenance_Payment_Method";

    public const string MaintenancePaymentStatus = "Maintenance_Payment_Status";
}
