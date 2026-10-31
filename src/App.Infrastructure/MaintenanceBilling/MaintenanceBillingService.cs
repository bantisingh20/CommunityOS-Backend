using App.Application.Abstractions;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.MaintenanceBilling;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainMaintenanceBill = App.Domain.MaintenanceBilling.MaintenanceBill;
using DomainMaintenanceBillStatuses = App.Domain.MaintenanceBilling.MaintenanceBillStatuses;
using DomainMaintenanceCharge = App.Domain.MaintenanceBilling.MaintenanceCharge;
using DomainMaintenancePayment = App.Domain.MaintenanceBilling.MaintenancePayment;

namespace App.Infrastructure.MaintenanceBilling;

public sealed class MaintenanceBillingService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IAuthorizationGuard authorizationGuard,
    ITenantContext tenant,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor)
    : IMaintenanceBillingService
{
    private const string DuplicateActiveChargeMessage =
        "An active maintenance charge already exists for this unit. Edit the existing charge instead.";
    private const string DuplicateBillMessage =
        "A maintenance bill already exists for this unit and period.";
    private const string DomainPendingApproval = "pending_approval";
    private const string DomainApproved = "approved";
    private const string DomainRejected = "rejected";

    private static readonly ListQueryConfiguration<DomainMaintenanceBill> BillQueryConfig =
        new ListQueryConfiguration<DomainMaintenanceBill>()
            .WithDefaultSort(b => b.IssuedAtUtc, SortDirection.Descending)
            .Sortable("issuedAtUtc", b => b.IssuedAtUtc)
            .Sortable("status", b => b.Status)
            .Sortable("periodYear", b => b.PeriodYear)
            .Sortable("periodMonth", b => b.PeriodMonth)
            .Sortable("amount", b => b.Amount)
            .Filterable("status", b => b.Status)
            .Filterable("unitId", b => b.UnitId)
            .Filterable("periodYear", b => b.PeriodYear)
            .Filterable("periodMonth", b => b.PeriodMonth);

    public async Task<MaintenanceChargeDto> SetChargeAsync(SetMaintenanceChargeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenanceCharge", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to set a maintenance charge.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.Amount < 0)
        {
            throw new ValidationException("amount", "The maintenance charge amount must not be negative.");
        }

        await RequireUnitInCommunityAsync(request.UnitId, request.CommunityId, cancellationToken);

        var existing = await db.MaintenanceCharges
            .FirstOrDefaultAsync(c => c.UnitId == request.UnitId && c.IsActive, cancellationToken);

        if (existing is not null)
        {
            existing.Amount = request.Amount;
            existing.Label = Trimmed(request.Label) ?? string.Empty;
            existing.IsActive = request.IsActive;

            await audit.AppendAsync(
                new AuditEntry("Update", "MaintenanceCharge", existing.Id.ToString(), existing.CommunityId,
                    Detail: $"unitId={existing.UnitId}; amount={existing.Amount}; active={existing.IsActive}"),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return ToDto(existing);
        }

        var charge = new DomainMaintenanceCharge
        {
            CommunityId = request.CommunityId,
            UnitId = request.UnitId,
            Amount = request.Amount,
            Label = Trimmed(request.Label) ?? string.Empty,
            IsActive = request.IsActive,
        };

        db.MaintenanceCharges.Add(charge);

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenanceCharge", charge.Id.ToString(), charge.CommunityId,
                Detail: $"unitId={charge.UnitId}; amount={charge.Amount}; active={charge.IsActive}"),
            cancellationToken);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateActiveChargeMessage);

        return ToDto(charge);
    }

    public async Task<BulkSetMaintenanceChargeResult> BulkSetChargesAsync(BulkSetMaintenanceChargeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenanceCharge", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to set maintenance charges.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.Amount < 0)
        {
            throw new ValidationException("amount", "The maintenance charge amount must not be negative.");
        }

        List<Guid> unitIds;
        if (request.ApplyToAllUnits)
        {
            unitIds = await db.Units.AsNoTracking()
                .Where(u => u.CommunityId == request.CommunityId)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
        }
        else
        {
            if (request.UnitIds is null || request.UnitIds.Count == 0)
            {
                throw new ValidationException("unitIds", "Select at least one unit, or choose apply-to-all.");
            }

            var distinct = request.UnitIds.Distinct().ToList();
            if (distinct.Count > 2000)
            {
                throw new ValidationException("unitIds", "Too many units in one request. Apply in smaller batches.");
            }

            var validIds = await db.Units.AsNoTracking()
                .Where(u => u.CommunityId == request.CommunityId && distinct.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            if (validIds.Count != distinct.Count)
            {
                throw new ValidationException("unitIds", "One or more units do not belong to this community.");
            }

            unitIds = validIds;
        }

        if (unitIds.Count == 0)
        {
            return new BulkSetMaintenanceChargeResult(request.CommunityId, 0, 0);
        }

        var label = Trimmed(request.Label) ?? string.Empty;

        var existing = await db.MaintenanceCharges
            .Where(c => c.CommunityId == request.CommunityId && c.IsActive && unitIds.Contains(c.UnitId))
            .ToListAsync(cancellationToken);

        var existingByUnit = existing.ToDictionary(c => c.UnitId);

        var created = 0;
        var updated = 0;

        foreach (var unitId in unitIds)
        {
            if (existingByUnit.TryGetValue(unitId, out var charge))
            {
                charge.Amount = request.Amount;
                charge.Label = label;
                charge.IsActive = request.IsActive;
                updated++;
            }
            else
            {
                db.MaintenanceCharges.Add(new DomainMaintenanceCharge
                {
                    CommunityId = request.CommunityId,
                    UnitId = unitId,
                    Amount = request.Amount,
                    Label = label,
                    IsActive = request.IsActive,
                });
                created++;
            }
        }

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenanceCharge", request.CommunityId.ToString(), request.CommunityId,
                Detail: $"bulk; created={created}; updated={updated}; amount={request.Amount}"),
            cancellationToken);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateActiveChargeMessage);

        return new BulkSetMaintenanceChargeResult(request.CommunityId, created, updated);
    }

    public async Task<IReadOnlyList<MaintenanceChargeDto>> ListChargesAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceCharge", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance charges.");
        }

        var charges = await db.MaintenanceCharges.AsNoTracking()
            .Where(c => c.CommunityId == communityId)
            .OrderBy(c => c.UnitId)
            .ToListAsync(cancellationToken);

        return charges.Select(ToDto).ToList();
    }

    public async Task<GenerateBillsResult> GenerateBillsAsync(GenerateBillsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to generate maintenance bills.");
        }

        authorizationGuard.EnsureCommunityAuthorized(request.CommunityId);

        if (request.PeriodMonth is < 1 or > 12)
        {
            throw new ValidationException("periodMonth", "The period month must be between 1 and 12.");
        }

        if (request.PeriodYear is < 2000 or > 3000)
        {
            throw new ValidationException("periodYear", "The period year is out of range.");
        }

        var charges = await db.MaintenanceCharges.AsNoTracking()
            .Where(c => c.CommunityId == request.CommunityId && c.IsActive && c.Amount > 0)
            .Select(c => new { c.UnitId, c.Amount, c.Label })
            .ToListAsync(cancellationToken);

        if (charges.Count == 0)
        {
            return new GenerateBillsResult(request.CommunityId, request.PeriodYear, request.PeriodMonth, 0, 0);
        }

        var alreadyBilledUnitIds = await db.MaintenanceBills.AsNoTracking()
            .Where(b => b.CommunityId == request.CommunityId
                && b.PeriodYear == request.PeriodYear
                && b.PeriodMonth == request.PeriodMonth)
            .Select(b => b.UnitId)
            .ToListAsync(cancellationToken);

        var billedSet = alreadyBilledUnitIds.Where(id => id is not null).Select(id => id!.Value).ToHashSet();

        var toCreate = charges.Where(c => !billedSet.Contains(c.UnitId)).ToList();
        var skipped = charges.Count - toCreate.Count;

        if (toCreate.Count == 0)
        {
            return new GenerateBillsResult(request.CommunityId, request.PeriodYear, request.PeriodMonth, 0, skipped);
        }

        var now = clock.UtcNow;

        foreach (var c in toCreate)
        {
            var bill = new DomainMaintenanceBill
            {
                CommunityId = request.CommunityId,
                UnitId = c.UnitId,
                PeriodYear = request.PeriodYear,
                PeriodMonth = request.PeriodMonth,
                Amount = c.Amount,
                AmountPaid = 0m,
                Label = c.Label,
                Status = DomainMaintenanceBillStatuses.Unpaid,
                DueDateUtc = request.DueDateUtc,
                IssuedAtUtc = now,
            };

            db.MaintenanceBills.Add(bill);
        }

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenanceBill", $"{request.CommunityId}:{request.PeriodYear}-{request.PeriodMonth}", request.CommunityId,
                Detail: $"generated={toCreate.Count}; skipped={skipped}"),
            cancellationToken);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateBillMessage);

        return new GenerateBillsResult(request.CommunityId, request.PeriodYear, request.PeriodMonth, toCreate.Count, skipped);
    }

    public Task<PagedData<MaintenanceBillDto>> ListBillsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return db.MaintenanceBills.AsNoTracking().ToPagedResultAsync(
            parameters, BillQueryConfig, b => ToDto(b), pageSizePolicy, executor, cancellationToken);
    }

    public async Task<MaintenanceBillDto?> GetBillAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        var bill = await db.MaintenanceBills.AsNoTracking().FirstOrDefaultAsync(b => b.Id == billId, cancellationToken);
        return bill is null ? null : ToDto(bill);
    }

    public async Task<MaintenancePaymentDto> RecordPaymentAsync(RecordMaintenancePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a maintenance payment.");
        }

        var bill = await db.MaintenanceBills.FirstOrDefaultAsync(b => b.Id == request.MaintenanceBillId, cancellationToken)
            ?? throw new NotFoundException("Maintenance bill not found.");

        authorizationGuard.EnsureCommunityAuthorized(bill.CommunityId);

        if (string.Equals(bill.Status, DomainMaintenanceBillStatuses.Paid, StringComparison.Ordinal)
            || string.Equals(bill.Status, DomainMaintenanceBillStatuses.Waived, StringComparison.Ordinal))
        {
            throw new ValidationException("maintenanceBillId", "The bill is already settled and cannot take further payments.");
        }

        if (request.Amount <= 0)
        {
            throw new ValidationException("amount", "The payment amount must be greater than zero.");
        }

        var method = await RequireValidCodeAsync(
            request.Method, MasterDataKeys.MaintenancePaymentMethod, bill.CommunityId, "method", "payment method", cancellationToken);

        var pendingTotal = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.MaintenanceBillId == bill.Id && p.Status == DomainPendingApproval)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var outstandingAfterPending = bill.Amount - bill.AmountPaid - pendingTotal;
        if (request.Amount > outstandingAfterPending)
        {
            throw new ValidationException("amount", $"The payment exceeds the collectable balance of {outstandingAfterPending}.");
        }

        var now = clock.UtcNow;
        var paidAt = request.PaidAtUtc ?? now;
        var approveNow = request.AutoApprove && !request.SubmittedByResident;

        var payment = new DomainMaintenancePayment
        {
            CommunityId = bill.CommunityId,
            MaintenanceBillId = bill.Id,
            UnitId = bill.UnitId,
            Amount = request.Amount,
            Method = method,
            Reference = Trimmed(request.Reference) ?? string.Empty,
            PaidAtUtc = paidAt,
            SubmittedByResident = request.SubmittedByResident,
            Status = approveNow ? DomainApproved : DomainPendingApproval,
        };

        if (approveNow)
        {
            payment.ApprovedByUserId = ParseUserId();
            payment.ApprovedAtUtc = now;
            ApplyPaymentToBill(bill, request.Amount);
        }

        db.MaintenancePayments.Add(payment);

        await audit.AppendAsync(
            new AuditEntry("Create", "MaintenancePayment", payment.Id.ToString(), payment.CommunityId,
                Detail: $"billId={bill.Id}; amount={request.Amount}; method={method}; status={payment.Status}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(payment);
    }

    public async Task<MaintenancePaymentDto> ApprovePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to approve a maintenance payment.");
        }

        var payment = await db.MaintenancePayments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Maintenance payment not found.");

        authorizationGuard.EnsureCommunityAuthorized(payment.CommunityId);

        if (!string.Equals(payment.Status, DomainPendingApproval, StringComparison.Ordinal))
        {
            throw new ValidationException("status", "Only a pending payment can be approved.");
        }

        var bill = await db.MaintenanceBills.FirstOrDefaultAsync(b => b.Id == payment.MaintenanceBillId, cancellationToken)
            ?? throw new NotFoundException("Maintenance bill not found.");

        if (string.Equals(bill.Status, DomainMaintenanceBillStatuses.Waived, StringComparison.Ordinal))
        {
            throw new ValidationException("status", "The bill has been waived and cannot take a payment.");
        }

        var outstanding = bill.Amount - bill.AmountPaid;
        if (payment.Amount > outstanding)
        {
            throw new ValidationException("amount", $"The payment exceeds the outstanding balance of {outstanding}.");
        }

        payment.Status = DomainApproved;
        payment.ApprovedByUserId = ParseUserId();
        payment.ApprovedAtUtc = clock.UtcNow;
        ApplyPaymentToBill(bill, payment.Amount);

        await audit.AppendAsync(
            new AuditEntry("Approve", "MaintenancePayment", payment.Id.ToString(), payment.CommunityId,
                Detail: $"billId={bill.Id}; amount={payment.Amount}; billStatus={bill.Status}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(payment);
    }

    public async Task<MaintenancePaymentDto> RejectPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to reject a maintenance payment.");
        }

        var payment = await db.MaintenancePayments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new NotFoundException("Maintenance payment not found.");

        authorizationGuard.EnsureCommunityAuthorized(payment.CommunityId);

        if (!string.Equals(payment.Status, DomainPendingApproval, StringComparison.Ordinal))
        {
            throw new ValidationException("status", "Only a pending payment can be rejected.");
        }

        payment.Status = DomainRejected;
        payment.ApprovedByUserId = ParseUserId();
        payment.ApprovedAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry("Reject", "MaintenancePayment", payment.Id.ToString(), payment.CommunityId,
                Detail: $"billId={payment.MaintenanceBillId}; amount={payment.Amount}"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(payment);
    }

    public async Task<IReadOnlyList<PendingPaymentDto>> ListPendingPaymentsAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view pending maintenance payments.");
        }

        var rows = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.CommunityId == communityId && p.Status == DomainPendingApproval)
            .Join(
                db.MaintenanceBills.AsNoTracking(),
                p => p.MaintenanceBillId,
                b => b.Id,
                (p, b) => new PendingPaymentDto(
                    p.Id, p.CommunityId, p.MaintenanceBillId, p.UnitId ?? Guid.Empty,
                    b.PeriodYear, b.PeriodMonth, p.Amount, p.Method, p.Reference, p.PaidAtUtc, p.SubmittedByResident))
            .OrderBy(x => x.PaidAtUtc)
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<MaintenancePaymentDto>> ListPaymentsForBillAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenancePayment", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance payments.");
        }

        var rows = await db.MaintenancePayments.AsNoTracking()
            .Where(p => p.MaintenanceBillId == billId)
            .OrderByDescending(p => p.PaidAtUtc).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<MaintenanceBillDto>> ListMyDueBillsAsync(int reminderDaysBeforeDue, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to view maintenance bills.");
        }

        var cutoff = clock.UtcNow.AddDays(reminderDaysBeforeDue <= 0 ? 10 : reminderDaysBeforeDue);

        var rows = await db.MaintenanceBills.AsNoTracking()
            .Where(b => (b.Status == DomainMaintenanceBillStatuses.Unpaid || b.Status == DomainMaintenanceBillStatuses.PartiallyPaid)
                && (b.DueDateUtc == null || b.DueDateUtc <= cutoff))
            .OrderBy(b => b.DueDateUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    private static void ApplyPaymentToBill(DomainMaintenanceBill bill, decimal amount)
    {
        bill.AmountPaid += amount;
        bill.Status = bill.AmountPaid >= bill.Amount
            ? DomainMaintenanceBillStatuses.Paid
            : DomainMaintenanceBillStatuses.PartiallyPaid;
    }

    private Guid? ParseUserId() =>
        Guid.TryParse(tenant.UserId, out var id) ? id : null;

    public async Task<MaintenanceBillDto> WaiveBillAsync(Guid billId, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "MaintenanceBill", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to waive a maintenance bill.");
        }

        var bill = await db.MaintenanceBills.FirstOrDefaultAsync(b => b.Id == billId, cancellationToken)
            ?? throw new NotFoundException("Maintenance bill not found.");

        authorizationGuard.EnsureCommunityAuthorized(bill.CommunityId);

        if (string.Equals(bill.Status, DomainMaintenanceBillStatuses.Paid, StringComparison.Ordinal))
        {
            throw new ValidationException("billId", "A fully paid bill cannot be waived.");
        }

        bill.Status = DomainMaintenanceBillStatuses.Waived;

        await audit.AppendAsync(
            new AuditEntry("Update", "MaintenanceBill", bill.Id.ToString(), bill.CommunityId, Detail: "status=waived"),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(bill);
    }

    private async Task RequireUnitInCommunityAsync(Guid unitId, Guid communityId, CancellationToken cancellationToken)
    {
        var unitCommunityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (unitCommunityId is null || unitCommunityId.Value != communityId)
        {
            throw new ValidationException("unitId", "The unit must belong to the same community.");
        }
    }

    private async Task<string> RequireValidCodeAsync(
        string? value, string listKey, Guid communityId, string field, string what, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, $"A {what} is required.");
        }

        var code = value.Trim();
        var allowed = await config.GetMasterDataAsync(listKey, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, code, StringComparison.Ordinal)))
        {
            throw new ValidationException(field, $"'{code}' is not a valid {what}.");
        }

        return code;
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static MaintenanceChargeDto ToDto(DomainMaintenanceCharge c) =>
        new(c.Id, c.CommunityId, c.UnitId, c.Amount, c.Label, c.IsActive);

    private static MaintenanceBillDto ToDto(DomainMaintenanceBill b) =>
        new(b.Id, b.CommunityId, b.UnitId ?? Guid.Empty, b.PeriodYear, b.PeriodMonth, b.Amount, b.AmountPaid,
            b.Amount - b.AmountPaid, b.Label, b.Status, b.DueDateUtc, b.IssuedAtUtc);

    private static MaintenancePaymentDto ToDto(DomainMaintenancePayment p) =>
        new(p.Id, p.CommunityId, p.MaintenanceBillId, p.UnitId ?? Guid.Empty, p.Amount, p.Method, p.Reference, p.PaidAtUtc,
            p.Status, p.SubmittedByResident, p.ApprovedAtUtc);
}
