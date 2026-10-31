using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Config;
using App.Application.MyTasks;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainVisitorStatuses = App.Domain.Gate.VisitorStatuses;
using DomainParcelStatuses = App.Domain.Parcel.ParcelStatuses;
using DomainSosAlertStatuses = App.Domain.Gate.SosAlertStatuses;
using DomainResidentVerificationStatuses = App.Domain.Residents.ResidentVerificationStatuses;
using DomainTicketStatuses = App.Domain.Helpdesk.TicketStatuses;
using DomainBillStatuses = App.Domain.MaintenanceBilling.MaintenanceBillStatuses;
using DomainPaymentStatuses = App.Domain.MaintenanceBilling.MaintenancePaymentStatuses;

namespace App.Infrastructure.MyTasks;

public sealed class MyTasksService(
    AppDbContext db,
    ITenantContext tenant,
    IAuthorizationGuard authorizationGuard,
    IConfigService config,
    IClock clock)
    : IMyTasksService
{
    public async Task<IReadOnlyList<MyTaskSummaryItem>> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<MyTaskSummaryItem>();
        var isResident = tenant.IsResidentSelfScoped;

        if (isResident)
        {
            var walkIns = await db.Visitors.AsNoTracking()
                .CountAsync(v => v.Status == DomainVisitorStatuses.Pending, cancellationToken);
            if (walkIns > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.WalkInApproval, "Walk-ins to approve", walkIns, "warning"));
            }

            var parcels = await db.Parcels.AsNoTracking()
                .CountAsync(p => p.Status == DomainParcelStatuses.ReadyForPickup, cancellationToken);
            if (parcels > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.ParcelPickup, "Parcels to collect", parcels, "info"));
            }

            var dueCutoff = clock.UtcNow.AddDays(await ReminderDaysAsync(cancellationToken));
            var dues = await db.MaintenanceBills.AsNoTracking()
                .CountAsync(b => (b.Status == DomainBillStatuses.Unpaid || b.Status == DomainBillStatuses.PartiallyPaid)
                    && (b.DueDateUtc == null || b.DueDateUtc <= dueCutoff), cancellationToken);
            if (dues > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.MaintenanceDue, "Maintenance dues to pay", dues, "warning"));
            }

            var kyc = await MyKycPendingCountAsync(cancellationToken);
            if (kyc > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.KycPending, "Complete your verification", kyc, "warning"));
            }
        }

        if (await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EntryExitEvent", cancellationToken))
        {
            var checkIns = await ApprovedAwaitingCheckInCountAsync(cancellationToken);
            if (checkIns > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.ApprovedWalkInCheckIn, "Approved walk-ins to check in", checkIns, "info"));
            }
        }

        if (await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "SosAlert", cancellationToken))
        {
            var sos = await db.SosAlerts.AsNoTracking()
                .CountAsync(s => s.Status == DomainSosAlertStatuses.Active, cancellationToken);
            if (sos > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.ActiveSos, "Active SOS alerts", sos, "urgent"));
            }
        }

        if (await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Resident", cancellationToken))
        {
            var toVerify = await db.Residents.AsNoTracking()
                .CountAsync(r => r.VerificationStatus == DomainResidentVerificationStatuses.Pending, cancellationToken);
            if (toVerify > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.ResidentToVerify, "Residents to verify", toVerify, "info"));
            }
        }

        if (await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, "Ticket", cancellationToken))
        {
            var openTickets = await db.Tickets.AsNoTracking()
                .CountAsync(t => t.Status == DomainTicketStatuses.New, cancellationToken);
            if (openTickets > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.OpenTicket, "Open tickets to triage", openTickets, "info"));
            }
        }

        if (await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            var pendingPayments = await db.MaintenancePayments.AsNoTracking()
                .CountAsync(p => p.Status == DomainPaymentStatuses.PendingApproval, cancellationToken);
            if (pendingPayments > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.PendingPaymentApproval, "Payments to approve", pendingPayments, "warning"));
            }

            var now = clock.UtcNow;
            var overdue = await db.MaintenanceBills.AsNoTracking()
                .CountAsync(b => (b.Status == DomainBillStatuses.Unpaid || b.Status == DomainBillStatuses.PartiallyPaid)
                    && b.DueDateUtc != null && b.DueDateUtc < now, cancellationToken);
            if (overdue > 0)
            {
                items.Add(new MyTaskSummaryItem(MyTaskTypes.OverdueBill, "Overdue bills", overdue, "warning"));
            }
        }

        return items;
    }

    public async Task<IReadOnlyList<MyTaskCard>> GetCardsAsync(string? type, CancellationToken cancellationToken = default)
    {
        var cards = new List<MyTaskCard>();
        var all = string.IsNullOrWhiteSpace(type) || string.Equals(type, "all", StringComparison.OrdinalIgnoreCase);
        var isResident = tenant.IsResidentSelfScoped;

        bool want(string t) => all || string.Equals(type, t, StringComparison.OrdinalIgnoreCase);

        if (isResident && want(MyTaskTypes.WalkInApproval))
        {
            var rows = await db.Visitors.AsNoTracking()
                .Where(v => v.Status == DomainVisitorStatuses.Pending)
                .OrderByDescending(v => v.CreatedAtUtc).Take(50)
                .Select(v => new { v.Id, v.Name, v.Category, v.Phone, v.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(v => new MyTaskCard(
                MyTaskTypes.WalkInApproval, v.Id.ToString(), v.Name, v.Category, v.Phone, null, null, v.Status)));
        }

        if (isResident && want(MyTaskTypes.ParcelPickup))
        {
            var rows = await db.Parcels.AsNoTracking()
                .Where(p => p.Status == DomainParcelStatuses.ReadyForPickup)
                .OrderByDescending(p => p.ReceivedAtUtc).Take(50)
                .Select(p => new { p.Id, p.TrackingNumber, p.Provider, p.ReceivedAtUtc, p.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(p => new MyTaskCard(
                MyTaskTypes.ParcelPickup, p.Id.ToString(), p.Provider, p.TrackingNumber, null, null, p.ReceivedAtUtc, p.Status)));
        }

        if (isResident && want(MyTaskTypes.MaintenanceDue))
        {
            var dueCutoff = clock.UtcNow.AddDays(await ReminderDaysAsync(cancellationToken));
            var rows = await db.MaintenanceBills.AsNoTracking()
                .Where(b => (b.Status == DomainBillStatuses.Unpaid || b.Status == DomainBillStatuses.PartiallyPaid)
                    && (b.DueDateUtc == null || b.DueDateUtc <= dueCutoff))
                .OrderBy(b => b.DueDateUtc).Take(50)
                .Select(b => new { b.Id, b.PeriodYear, b.PeriodMonth, b.Amount, b.AmountPaid, b.DueDateUtc, b.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(b => new MyTaskCard(
                MyTaskTypes.MaintenanceDue, b.Id.ToString(),
                $"Maintenance {b.PeriodMonth:00}/{b.PeriodYear}",
                $"Due {b.Amount - b.AmountPaid}",
                null, b.Amount - b.AmountPaid, b.DueDateUtc, b.Status)));
        }

        if (isResident && want(MyTaskTypes.KycPending))
        {
            var kyc = await MyKycPendingCountAsync(cancellationToken);
            if (kyc > 0)
            {
                cards.Add(new MyTaskCard(MyTaskTypes.KycPending, "me", "Complete your verification",
                    "Submit your KYC to unlock all features", null, null, null, null));
            }
        }

        if (want(MyTaskTypes.ApprovedWalkInCheckIn)
            && await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EntryExitEvent", cancellationToken))
        {
            var onSite = await OnSiteVisitorIdsAsync(cancellationToken);
            var rows = await db.Visitors.AsNoTracking()
                .Where(v => v.Status == DomainVisitorStatuses.Approved)
                .OrderByDescending(v => v.CreatedAtUtc).Take(100)
                .Select(v => new { v.Id, v.Name, v.Category, v.Phone, v.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows
                .Where(v => !onSite.Contains(v.Id))
                .Take(50)
                .Select(v => new MyTaskCard(
                    MyTaskTypes.ApprovedWalkInCheckIn, v.Id.ToString(), v.Name, v.Category, v.Phone, null, null, v.Status)));
        }

        if (want(MyTaskTypes.ActiveSos)
            && await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "SosAlert", cancellationToken))
        {
            var rows = await db.SosAlerts.AsNoTracking()
                .Where(s => s.Status == DomainSosAlertStatuses.Active)
                .OrderByDescending(s => s.RaisedAtUtc).Take(50)
                .Select(s => new { s.Id, s.Location, s.RaisedAtUtc, s.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(s => new MyTaskCard(
                MyTaskTypes.ActiveSos, s.Id.ToString(), "SOS alert", s.Location ?? "Location not provided", null, null, s.RaisedAtUtc, s.Status)));
        }

        if (want(MyTaskTypes.ResidentToVerify)
            && await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Resident", cancellationToken))
        {
            var rows = await db.Residents.AsNoTracking()
                .Where(r => r.VerificationStatus == DomainResidentVerificationStatuses.Pending)
                .OrderBy(r => r.Name).Take(50)
                .Select(r => new { r.Id, r.Name, r.VerificationStatus })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(r => new MyTaskCard(
                MyTaskTypes.ResidentToVerify, r.Id.ToString(), r.Name, "Pending verification", null, null, null, r.VerificationStatus)));
        }

        if (want(MyTaskTypes.OpenTicket)
            && await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, "Ticket", cancellationToken))
        {
            var rows = await db.Tickets.AsNoTracking()
                .Where(t => t.Status == DomainTicketStatuses.New)
                .OrderByDescending(t => t.CreatedAtUtc).Take(50)
                .Select(t => new { t.Id, t.Title, t.Category, t.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(t => new MyTaskCard(
                MyTaskTypes.OpenTicket, t.Id.ToString(), t.Title, t.Category, null, null, null, t.Status)));
        }

        if (want(MyTaskTypes.PendingPaymentApproval)
            && await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            var rows = await db.MaintenancePayments.AsNoTracking()
                .Where(p => p.Status == DomainPaymentStatuses.PendingApproval)
                .OrderBy(p => p.PaidAtUtc).Take(50)
                .Select(p => new { p.Id, p.Amount, p.Method, p.Reference, p.PaidAtUtc, p.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(p => new MyTaskCard(
                MyTaskTypes.PendingPaymentApproval, p.Id.ToString(), $"Payment {p.Amount}", p.Method,
                p.Reference, p.Amount, p.PaidAtUtc, p.Status)));
        }

        if (want(MyTaskTypes.OverdueBill)
            && await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            var now = clock.UtcNow;
            var rows = await db.MaintenanceBills.AsNoTracking()
                .Where(b => (b.Status == DomainBillStatuses.Unpaid || b.Status == DomainBillStatuses.PartiallyPaid)
                    && b.DueDateUtc != null && b.DueDateUtc < now)
                .OrderBy(b => b.DueDateUtc).Take(50)
                .Select(b => new { b.Id, b.PeriodYear, b.PeriodMonth, b.Amount, b.AmountPaid, b.DueDateUtc, b.Status })
                .ToListAsync(cancellationToken);
            cards.AddRange(rows.Select(b => new MyTaskCard(
                MyTaskTypes.OverdueBill, b.Id.ToString(),
                $"Overdue {b.PeriodMonth:00}/{b.PeriodYear}", $"Due {b.Amount - b.AmountPaid}",
                null, b.Amount - b.AmountPaid, b.DueDateUtc, b.Status)));
        }

        return cards;
    }

    private async Task<int> ReminderDaysAsync(CancellationToken cancellationToken)
    {
        var days = await config.GetIntAsync(ConfigKeys.MaintenanceBillReminderDaysBeforeDue, 10, null, cancellationToken);
        return days <= 0 ? 10 : days;
    }

    private async Task<int> MyKycPendingCountAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenant.UserId, out var userId))
        {
            return 0;
        }

        return await db.Residents.AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.VerificationStatus != DomainResidentVerificationStatuses.Verified, cancellationToken);
    }

    private async Task<int> ApprovedAwaitingCheckInCountAsync(CancellationToken cancellationToken)
    {
        var onSite = await OnSiteVisitorIdsAsync(cancellationToken);
        var approvedIds = await db.Visitors.AsNoTracking()
            .Where(v => v.Status == DomainVisitorStatuses.Approved)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        return approvedIds.Count(id => !onSite.Contains(id));
    }

    private async Task<HashSet<Guid>> OnSiteVisitorIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await db.EntryExitEvents.AsNoTracking()
            .Where(e => e.ExitTsUtc == null && e.VisitorId != null)
            .Select(e => e.VisitorId!.Value)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}
