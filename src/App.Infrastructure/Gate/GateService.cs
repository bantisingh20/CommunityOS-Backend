using System.Security.Cryptography;
using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Auth;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Files;
using App.Application.Gate;
using App.Application.Messaging;
using App.Application.Notifications;
using App.Application.Querying;
using App.Contracts.Envelope;
using App.Domain.Files;
using App.Domain.Gate;
using App.Domain.Identity;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Infrastructure.Gate;



public sealed class GateService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IPasswordHasher hasher,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    IMessageSender messages,
    IOptions<SmtpOptions> smtpOptions,
    ISecurityOpsService securityOps,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor,
    IFileBlobStore blobStore,
    ITenantContext tenant)
    : IGateService
{
    private static readonly ListQueryConfiguration<VisitPass> VisitPassQueryConfig =
        new ListQueryConfiguration<VisitPass>()
            .WithDefaultSort(p => p.ValidUntilUtc, SortDirection.Descending)
            .Sortable("validUntilUtc", p => p.ValidUntilUtc)
            .Sortable("validFromUtc", p => p.ValidFromUtc)
            .Sortable("status", p => p.Status)
            .Sortable("visitorName", p => p.VisitorName)
            .Filterable("status", p => p.Status)
            .Filterable("hostUnitId", p => p.UnitId)
            .Searchable(p => p.VisitorName);

    private static readonly ListQueryConfiguration<Visitor> VisitorQueryConfig =
        new ListQueryConfiguration<Visitor>()
            .WithDefaultSort(v => v.Status)
            .Sortable("status", v => v.Status)
            .Sortable("category", v => v.Category)
            .Sortable("name", v => v.Name)
            .Filterable("status", v => v.Status)
            .Filterable("category", v => v.Category)
            .Filterable("hostUnitId", v => v.UnitId)
            // Registered date (createdAtUtc) is filterable so the walk-in list can be scoped to a day
            // (fromDate/toDate via >= / <= on the endpoint). Default-today lives in the client.
            .Filterable("createdAtUtc", v => v.CreatedAtUtc)
            .Searchable(v => v.Name);

    private static readonly ListQueryConfiguration<EntryExitEvent> EntryExitEventQueryConfig =
        new ListQueryConfiguration<EntryExitEvent>()
            // Newest-first chronological log by default, riding the (CommunityId, EntryTsUtc) index.
            .WithDefaultSort(e => e.EntryTsUtc, SortDirection.Descending)
            .Sortable("entryTsUtc", e => e.EntryTsUtc)
            .Sortable("exitTsUtc", e => e.ExitTsUtc)
            .Filterable("category", e => e.Category)
            .Filterable("destinationUnitId", e => e.DestinationUnitId)
            .Filterable("visitorId", e => e.VisitorId)
            // "open" filter (true = still on-site): map the client's bool to the exit-null predicate
            // via IsOpen so the entry/exit log can show currently-on-site visitors (Req 25.5).
            .Filterable("open", e => e.ExitTsUtc == null)
            .Searchable(e => e.VisitorName);

    private static readonly ListQueryConfiguration<RecurringVisitor> RecurringVisitorQueryConfig =
        new ListQueryConfiguration<RecurringVisitor>()
            .WithDefaultSort(r => r.Name)
            .Sortable("name", r => r.Name)
            .Sortable("status", r => r.Status)
            .Sortable("category", r => r.Category)
            .Filterable("status", r => r.Status)
            .Filterable("category", r => r.Category)
            .Filterable("hostUnitId", r => r.UnitId)
            .Searchable(r => r.Name);
    private const string WalkInApprovalRequiredKey = "gate.walkIn.approvalRequired";
    private const bool DefaultWalkInApprovalRequired = true;
    private const string WalkInPendingExpiryMinutesKey = "gate.walkIn.pendingExpiryMinutes";
    private const int DefaultWalkInPendingExpiryMinutes = 15;
    private const int MaxPendingExpirySweepBatch = 500;
    private const string DefaultValidityHoursKey = "gate.visitPass.defaultValidityHours";
    private const int DefaultValidityHours = 24;
    private const string DefaultValidityKey = "gate.visitPass.defaultValidity";
    private const string DefaultValidityUnitKey = "gate.visitPass.defaultValidityUnit";
    private const string DefaultMaxUsesKey = "gate.visitPass.defaultMaxUses";
    private const int DefaultMaxUses = 1;
    private const string RecurringExpiringSoonDaysKey = "gate.recurringVisitor.expiringSoonDays";
    private const int DefaultRecurringExpiringSoonDays = 10;

    // How many QR-token generations to try before giving up on a (vanishingly unlikely) collision.
    private const int QrTokenAttempts = 5;

    public async Task<CreateVisitPassResult> CreateVisitPassAsync(CreateVisitPassRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var visitorName = RequireText(request.VisitorName, "visitorName", "A visitor name is required.");

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "VisitPass", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a visit pass.");
        }

        var communityId = await RequireHostUnitCommunityAsync(request.HostUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);

        var now = clock.UtcNow;
        var validFrom = request.ValidFromUtc ?? now;
        var validity = await ResolveDefaultValidityAsync(communityId, cancellationToken);
        var validUntil = request.ValidUntilUtc ?? validity.Apply(validFrom);
        if (validUntil <= validFrom)
        {
            throw new ValidationException("validUntilUtc", "The pass valid-until time must be after the valid-from time.");
        }

        var defaultMaxUses = await config.GetIntAsync(DefaultMaxUsesKey, DefaultMaxUses, communityId, cancellationToken);
        var maxUses = request.MaxUses is { } m && m >= 1 ? m : Math.Max(1, defaultMaxUses);

        if (request.NumberOfPersons is int persons && persons <= 0)
        {
            throw new ValidationException("numberOfPersons", "Number of persons must be greater than zero.");
        }

        // Optional vehicle type: validate against the editable Vehicle_Type list when supplied.
        if (!string.IsNullOrWhiteSpace(request.VehicleType))
        {
            var allowedVehicleTypes = await config.GetMasterDataAsync(MasterDataKeys.VehicleType, communityId, cancellationToken: cancellationToken);
            if (!allowedVehicleTypes.Any(e => string.Equals(e.Code, request.VehicleType.Trim(), StringComparison.Ordinal)))
            {
                throw new ValidationException("vehicleType", $"'{request.VehicleType}' is not a valid vehicle type.");
            }
        }

        // Short OTP shared with the visitor; only its salted hash is stored (steering: secrets).
        var otp = GenerateOtp();

        var pass = new VisitPass
        {
            CommunityId = communityId,
            HostUnitId = request.HostUnitId,
            HostResidentId = request.HostResidentId,
            VisitorName = visitorName,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? null : request.VehicleNumber.Trim(),
            VehicleType = string.IsNullOrWhiteSpace(request.VehicleType) ? null : request.VehicleType.Trim(),
            NumberOfPersons = request.NumberOfPersons,
            WhomToMeet = string.IsNullOrWhiteSpace(request.WhomToMeet) ? null : request.WhomToMeet.Trim(),
            VisitDateUtc = request.VisitDateUtc,
            PhotoFileId = request.PhotoFileId,
            OtpHash = hasher.Hash(otp),
            PublicToken = GenerateQrToken(),
            ValidFromUtc = validFrom,
            ValidUntilUtc = validUntil,
            MaxUses = maxUses,
            UseCount = 0,
            Status = VisitPassStatuses.Active,
        };

        await SaveWithUniqueQrTokenAsync(pass, cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "VisitPass", pass.Id.ToString(), pass.CommunityId, Detail: null),
            cancellationToken);

        await NotifyVisitorOfPassAsync(pass, otp, cancellationToken);

        return new CreateVisitPassResult(ToDto(pass), otp);
    }

    private async Task NotifyVisitorOfPassAsync(VisitPass pass, string otp, CancellationToken cancellationToken)
    {
        var validUntil = pass.ValidUntilUtc.ToString("u");
        var smsText =
            $"You have a visit pass for {pass.VisitorName}. Show this at the gate. Code (OTP): {otp}. Pass: {pass.QrToken}. Valid until {validUntil} (UTC).";
        var emailSubject = "Your visit pass";
        var emailBody =
            $"Hello {pass.VisitorName},\n\nYou have been invited to visit. Please show this at the gate.\n\nOne-time code (OTP): {otp}\nPass token (QR): {pass.QrToken}\nValid until: {validUntil} (UTC)\n\nThank you.";

        var queued = false;

        if (!string.IsNullOrWhiteSpace(pass.Phone))
        {
            try
            {
                await messages.QueueSmsAsync(pass.Phone, smsText, cancellationToken);
                queued = true;
            }
            catch (ValidationException)
            {
            }
        }

        if (smtpOptions.Value.IsEnabled && !string.IsNullOrWhiteSpace(pass.Email))
        {
            try
            {
                await messages.QueueEmailAsync(pass.Email, emailSubject, emailBody, cancellationToken);
                queued = true;
            }
            catch (ValidationException)
            {
            }
        }

        if (queued)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private const string PublicBaseUrlKey = "gate.visitPass.publicBaseUrl";
    private const string DefaultPublicBaseUrl = "http://localhost:5173";

    public async Task<PublicVisitPassDto?> GetPublicPassAsync(string publicToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            return null;
        }

        var pass = await db.VisitPasses.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.PublicToken == publicToken && !p.IsDeleted, cancellationToken);
        if (pass is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        var isValidNow =
            string.Equals(pass.Status, VisitPassStatuses.Active, StringComparison.Ordinal) &&
            pass.UseCount < pass.MaxUses &&
            now >= pass.ValidFromUtc &&
            now <= pass.ValidUntilUtc;

        string? unitNumber = null;
        if (pass.UnitId is { } unitId)
        {
            unitNumber = await db.Units.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == unitId).Select(u => u.UnitNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        string? communityName = null;
        string? organizationName = null;
        var community = await db.Communities.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == pass.CommunityId)
            .Select(c => new { c.Name, c.OrganizationId })
            .FirstOrDefaultAsync(cancellationToken);
        if (community is not null)
        {
            communityName = community.Name;
            organizationName = await db.Organizations.IgnoreQueryFilters().AsNoTracking()
                .Where(o => o.Id == community.OrganizationId).Select(o => o.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var photoDataUrl = await ReadPhotoDataUrlAsync(pass.PhotoFileId, cancellationToken);

        return new PublicVisitPassDto(
            pass.VisitorName,
            photoDataUrl,
            pass.QrToken,
            pass.ValidFromUtc,
            pass.ValidUntilUtc,
            pass.Status,
            isValidNow,
            pass.NumberOfPersons,
            pass.WhomToMeet,
            pass.VehicleNumber,
            unitNumber,
            communityName,
            organizationName);
    }

    private async Task<string?> ReadPhotoDataUrlAsync(Guid? photoFileId, CancellationToken cancellationToken)
    {
        if (photoFileId is not { } fileId)
        {
            return null;
        }
        var file = await db.Set<StoredFile>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted, cancellationToken);
        if (file is null)
        {
            return null;
        }
        await using var stream = await blobStore.OpenAsync(file.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        var base64 = Convert.ToBase64String(ms.ToArray());
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "image/jpeg" : file.ContentType;
        return $"data:{contentType};base64,{base64}";
    }

    public async Task<SharePassResult> SharePassAsync(Guid passId, SharePassRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "VisitPass", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to share a visit pass.");
        }

        var pass = await db.VisitPasses.FirstOrDefaultAsync(p => p.Id == passId, cancellationToken)
            ?? throw new NotFoundException("Visit pass not found.");
        authorizationGuard.EnsureCommunityAuthorized(pass.CommunityId);

        var baseUrl = (await config.GetStringAsync(PublicBaseUrlKey, DefaultPublicBaseUrl, pass.CommunityId, cancellationToken)).TrimEnd('/');
        var publicUrl = $"{baseUrl}/pass/{pass.PublicToken}";

        var toPhone = string.IsNullOrWhiteSpace(request.ToPhone) ? pass.Phone : request.ToPhone.Trim();
        var toEmail = string.IsNullOrWhiteSpace(request.ToEmail) ? pass.Email : request.ToEmail.Trim();

        var validUntil = pass.ValidUntilUtc.ToString("u");
        var message =
            $"Your visit pass for {pass.VisitorName}. View it here: {publicUrl} (valid until {validUntil} UTC).";

        var smsQueued = false;
        var whatsAppQueued = false;
        var emailQueued = false;

        if (request.Sms && !string.IsNullOrWhiteSpace(toPhone))
        {
            try { await messages.QueueSmsAsync(toPhone, message, cancellationToken); smsQueued = true; }
            catch (ValidationException) { }
        }
        if (request.WhatsApp && !string.IsNullOrWhiteSpace(toPhone))
        {
            try { await messages.QueueWhatsAppAsync(toPhone, message, cancellationToken); whatsAppQueued = true; }
            catch (ValidationException) { }
        }
        if (request.Email && !string.IsNullOrWhiteSpace(toEmail))
        {
            try
            {
                await messages.QueueEmailAsync(toEmail, "Your visit pass", $"Hello {pass.VisitorName},\n\n{message}\n\nThank you.", cancellationToken);
                emailQueued = true;
            }
            catch (ValidationException) { }
        }

        if (smsQueued || whatsAppQueued || emailQueued)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new SharePassResult(publicUrl, smsQueued, whatsAppQueued, emailQueued);
    }

    public async Task<VerifyVisitPassResult> VerifyVisitPassAsync(VerifyVisitPassRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Visitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to verify a visit pass.");
        }

        var byQr = !string.IsNullOrWhiteSpace(request.QrToken);
        var byOtp = !string.IsNullOrWhiteSpace(request.Otp);
        if (!byQr && !byOtp)
        {
            throw new ValidationException("qrToken", "A QR token or OTP is required to verify a visit pass.");
        }

        // TRACKED load (not AsNoTracking): on accept we increment UseCount and need EF to carry the
        // RowVersion concurrency token into the UPDATE (Req 23.3 — concurrency-safe use count). The
        // global tenant filter scopes the lookup to the caller's communities.
        VisitPass? pass;
        if (byQr)
        {
            var qr = request.QrToken!.Trim();
            // Rides the (CommunityId, QrToken) unique index directly.
            pass = await db.VisitPasses.FirstOrDefaultAsync(p => p.QrToken == qr, cancellationToken);
        }
        else
        {
            // OTP path: the OTP hash is salted (not equality-searchable), so load the community's
            // still-usable passes and verify in constant time. ponytail: this is an O(active-passes)
            // hash scan per OTP verify; the primary gate path is the indexed QR scan above, and OTP is
            // the manual fallback — if OTP volume ever grows, add a lookup token. Upgrade path: store a
            // separate indexed opaque OTP-lookup code alongside the hash.
            var otp = request.Otp!.Trim();
            var now0 = clock.UtcNow;
            var candidates = await db.VisitPasses
                .Where(p => p.Status == VisitPassStatuses.Active
                    && p.ValidFromUtc <= now0
                    && p.ValidUntilUtc >= now0
                    && p.UseCount < p.MaxUses)
                .ToListAsync(cancellationToken);
            pass = candidates.FirstOrDefault(p => hasher.Verify(p.OtpHash, otp));
        }

        if (pass is null)
        {
            // Unknown token/OTP (or outside the caller's tenant scope): reject without leaking which.
            return Reject("The visit pass is invalid.");
        }

        // Evaluate the pass state (Req 23.4). Order: revoked → window → uses. Expiry is derived from
        // the window (Req 23.2), not a stored flag, so a lapsed active pass still reads as expired.
        var now = clock.UtcNow;
        if (string.Equals(pass.Status, VisitPassStatuses.Revoked, StringComparison.Ordinal))
        {
            return Reject("The visit pass has been revoked.");
        }

        if (now < pass.ValidFromUtc)
        {
            return Reject("The visit pass is not yet valid.");
        }

        if (now > pass.ValidUntilUtc)
        {
            return Reject("The visit pass has expired.");
        }

        if (!string.Equals(pass.Status, VisitPassStatuses.Active, StringComparison.Ordinal) || pass.UseCount >= pass.MaxUses)
        {
            return Reject("The visit pass has already been used.");
        }

        // Accept (Req 23.3): increment the use count and mark the pass used when exhausted. The
        // RowVersion makes this concurrency-safe — a second concurrent scan loses the row-version
        // check and its save maps to CONFLICT (central mapping in AppDbContext), so a single-use pass
        // is admitted exactly once.
        pass.UseCount += 1;
        if (pass.UseCount >= pass.MaxUses)
        {
            pass.Status = VisitPassStatuses.Used;
        }

        await audit.AppendAsync(
            new AuditEntry("Verify", "VisitPass", pass.Id.ToString(), pass.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VerifyVisitPassResult(Accepted: true, RejectionReason: null, Pass: ToDto(pass));
    }

    public async Task<RegisterWalkInResult> RegisterWalkInAsync(RegisterWalkInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = RequireText(request.Name, "name", "A visitor name is required.");

        // Defense-in-depth (Req 24.1): a guard registers a walk-in with Create on Visitor.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Visitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to register a walk-in visitor.");
        }

        var communityId = await RequireHostUnitCommunityAsync(request.HostUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);
        await RequireValidCategoryAsync(request.Category, communityId, cancellationToken);
        await RequireValidIdProofTypeAsync(request.IdProofType, communityId, cancellationToken);

        if (request.ExpectedDurationMinutes is int duration && duration <= 0)
        {
            throw new ValidationException("expectedDurationMinutes", "Expected duration must be greater than zero.");
        }

        var visitor = new Visitor
        {
            CommunityId = communityId,
            HostUnitId = request.HostUnitId,
            Name = name,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Category = request.Category.Trim(),
            PhotoFileId = request.PhotoFileId,
            VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? null : request.VehicleNumber.Trim(),
            GateLocation = string.IsNullOrWhiteSpace(request.GateLocation) ? null : request.GateLocation.Trim(),
            IdProofType = string.IsNullOrWhiteSpace(request.IdProofType) ? null : request.IdProofType.Trim(),
            IdProofNumber = string.IsNullOrWhiteSpace(request.IdProofNumber) ? null : request.IdProofNumber.Trim(),
            Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? null : request.Purpose.Trim(),
            WhomToMeet = string.IsNullOrWhiteSpace(request.WhomToMeet) ? null : request.WhomToMeet.Trim(),
            ExpectedDurationMinutes = request.ExpectedDurationMinutes,
            // Starts pending: entry withheld until a resident approves (Req 24.3, 24.5).
            Status = VisitorStatuses.Pending,
        };

        var approvalRequired = await config.GetBoolAsync(WalkInApprovalRequiredKey, DefaultWalkInApprovalRequired, communityId, cancellationToken);
        string? autoOtp = null;
        if (!approvalRequired)
        {
            visitor.Status = VisitorStatuses.Approved;
            visitor.ApprovalDecisionAtUtc = clock.UtcNow;
            autoOtp = GenerateOtp();
            visitor.OtpHash = hasher.Hash(autoOtp);
            visitor.QrToken = await GenerateUniqueVisitorQrTokenAsync(communityId, cancellationToken);
        }

        db.Visitors.Add(visitor);

        // Watchlist flag (Req 26.5): check the walk-in's name / phone / vehicle number against the
        // community's ACTIVE watchlist. A match is FLAGGED (recorded in the audit trail so the guard
        // and the resident see it) — registration itself proceeds (the walk-in is pending anyway and
        // entry is withheld until approval). The hard override gate is enforced at RecordEntry, where
        // admitting a watchlisted subject requires the Approve permission (Req 26.5).
        var walkInMatch = await MatchWatchlistAnyAsync(
            visitor.CommunityId,
            cancellationToken,
            visitor.Name,
            visitor.Phone,
            visitor.VehicleNumber);
        if (walkInMatch)
        {
            await audit.AppendAsync(
                new AuditEntry("WatchlistFlag", "Visitor", visitor.Id.ToString(), visitor.CommunityId,
                    Detail: "Walk-in matched an active watchlist entry; entry requires an authorized override."),
                cancellationToken);
        }

        await audit.AppendAsync(
            new AuditEntry("Create", "Visitor", visitor.Id.ToString(), visitor.CommunityId, Detail: null),
            cancellationToken);

        if (approvalRequired)
        {
            // Notify the host unit's residents to approve/reject (Req 24.2). EnqueueAsync only Adds the
            // outbox rows to THIS DbContext (no save), so the single SaveChangesAsync below commits the
            // walk-in + the notifications atomically (transactional outbox) — a rollback queues nothing.
            await NotifyHostUnitResidentsAsync(visitor, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new RegisterWalkInResult(ToDto(visitor), AutoApproved: false, Otp: null);
        }

        await NotifyVisitorOfApprovalAsync(visitor, autoOtp!, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await RecordEntryAsync(new RecordEntryRequest(visitor.Id, null, null, null, null, null), cancellationToken);

        return new RegisterWalkInResult(ToDto(visitor), AutoApproved: true, Otp: autoOtp);
    }

    public Task<VisitorDto> ApproveWalkInAsync(Guid visitorId, Guid approvingResidentId, CancellationToken cancellationToken = default) =>
        DecideWalkInAsync(
            visitorId,
            approvingResidentId,
            toStatus: VisitorStatuses.Approved,
            auditAction: "Approve",
            detail: null,
            cancellationToken);

    public Task<VisitorDto> RejectWalkInAsync(Guid visitorId, Guid rejectingResidentId, RejectWalkInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DecideWalkInAsync(
            visitorId,
            rejectingResidentId,
            toStatus: VisitorStatuses.Rejected,
            auditAction: "Reject",
            detail: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            cancellationToken);
    }

    // =============================================================================================
    // Task 16.2 — recurring visitors (Req 25.2) + entry/exit capture (Req 25.3, 25.4)
    // =============================================================================================

    public async Task<RecurringVisitorDto> CreateRecurringVisitorAsync(CreateRecurringVisitorRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = RequireText(request.Name, "name", "A visitor name is required.");

        // Defense-in-depth (Req 25.2): a resident defines a recurring visitor for their OWN unit with
        // Create on RecurringVisitor. The endpoint gate (Task 16.4) declares the same requirement; the
        // invariant must hold regardless of caller — no permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "RecurringVisitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a recurring visitor.");
        }

        // Resolve the host unit (tenant + resident self-scope filtered) → its community anchors the
        // row and runs the own-unit/same-community check (a cross-unit host → not found).
        var communityId = await RequireHostUnitCommunityAsync(request.HostUnitId, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(communityId);
        await RequireValidCategoryAsync(request.Category, communityId, cancellationToken);
        ValidateSchedule(request);

        var recurring = new RecurringVisitor
        {
            CommunityId = communityId,
            HostUnitId = request.HostUnitId,
            HostResidentId = request.HostResidentId,
            Name = name,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PhotoFileId = request.PhotoFileId,
            Category = request.Category.Trim(),
            DaysOfWeek = request.DaysOfWeek,
            StartDate = request.StartDate.Date,
            EndDate = request.EndDate.Date,
            DailyStartMinutes = request.DailyStartMinutes,
            DailyEndMinutes = request.DailyEndMinutes,
            Status = RecurringVisitorStatuses.Active,
        };

        db.RecurringVisitors.Add(recurring);

        await audit.AppendAsync(
            new AuditEntry("Create", "RecurringVisitor", recurring.Id.ToString(), recurring.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(recurring);
    }

    public async Task<RecurringVisitorDto> RevokeRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default)
    {
        // Defense-in-depth (Req 25.2): a resident revokes their own recurring visitor with Create on
        // RecurringVisitor (the same grant that created it).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "RecurringVisitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to revoke a recurring visitor.");
        }

        // TRACKED load: we mutate the row and need the RowVersion carried into the UPDATE. The global
        // tenant + resident self-scope filter restricts this to the caller's own unit, so a
        // cross-unit/foreign-tenant recurring visitor is a not-found.
        var recurring = await db.RecurringVisitors.FirstOrDefaultAsync(r => r.Id == recurringVisitorId, cancellationToken)
            ?? throw new NotFoundException("Recurring visitor not found.");

        authorizationGuard.EnsureCommunityAuthorized(recurring.CommunityId);

        // Only an active authorization can be revoked. A revoke of an already-revoked one conflicts
        // with the current state — the same thing a concurrency loser sees after the winner revoked.
        if (!recurring.IsActive)
        {
            throw new ConflictException(
                $"The recurring visitor is '{recurring.Status}', not 'active'; it cannot be revoked again.");
        }

        recurring.Status = RecurringVisitorStatuses.Revoked;

        await audit.AppendAsync(
            new AuditEntry("Revoke", "RecurringVisitor", recurring.Id.ToString(), recurring.CommunityId, Detail: null),
            cancellationToken);

        // RowVersion makes the revoke record exactly once under concurrency (loser → CONFLICT).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(recurring);
    }

    public async Task<int> GetRecurringExpiringSoonDaysAsync(Guid? communityId, CancellationToken cancellationToken = default)
    {
        var days = await config.GetIntAsync(RecurringExpiringSoonDaysKey, DefaultRecurringExpiringSoonDays, communityId, cancellationToken);
        return days >= 0 ? days : DefaultRecurringExpiringSoonDays;
    }

    public async Task<RecurringVisitorDto> ExtendRecurringVisitorAsync(Guid recurringVisitorId, DateTime newEndDateUtc, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "RecurringVisitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to extend a recurring visitor.");
        }

        var recurring = await db.RecurringVisitors.FirstOrDefaultAsync(r => r.Id == recurringVisitorId, cancellationToken)
            ?? throw new NotFoundException("Recurring visitor not found.");

        authorizationGuard.EnsureCommunityAuthorized(recurring.CommunityId);

        var newEnd = newEndDateUtc.Date;
        if (newEnd < recurring.StartDate.Date)
        {
            throw new ValidationException("endDate", "The new end date must be on or after the start date.");
        }

        if (newEnd < clock.UtcNow.Date)
        {
            throw new ValidationException("endDate", "The new end date must be today or in the future.");
        }

        recurring.EndDate = newEnd;
        recurring.Status = RecurringVisitorStatuses.Active;

        await audit.AppendAsync(
            new AuditEntry("Extend", "RecurringVisitor", recurring.Id.ToString(), recurring.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(recurring);
    }

    public async Task<bool> IsRecurringAuthorizedAsync(Guid recurringVisitorId, DateTime whenUtc, CancellationToken cancellationToken = default)
    {
        // Read-only authorization check (Req 25.2): tenant-scoped AsNoTracking read; the domain rule
        // (active + in range + matching day + optional daily window) lives on the entity. A missing /
        // foreign-tenant id is simply not authorized (the global filter hides it → null → false).
        var recurring = await db.RecurringVisitors.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recurringVisitorId, cancellationToken);

        return recurring?.IsAuthorizedOn(whenUtc) ?? false;
    }

    public async Task<EntryExitEventDto> RecordEntryAsync(RecordEntryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A guard records an entry with Create on EntryExitEvent (defense-in-depth over the Task 16.4
        // endpoint gate).
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EntryExitEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record an entry.");
        }

        var kind = RequireExactlyOneAuthorization(request.VisitorId, request.VisitPassId, request.RecurringVisitorId);
        var now = clock.UtcNow;

        // Resolve + ENFORCE admittability for the named authorization (Req 24.4, 25.3): a non-
        // admittable authorization yields no entry event. The resolver returns the owning community
        // and the denormalized name/category captured onto the event.
        var admit = await ResolveAdmittableAuthorizationAsync(kind, request.VisitorId, request.VisitPassId, request.RecurringVisitorId, now, cancellationToken);
        authorizationGuard.EnsureCommunityAuthorized(admit.CommunityId);

        // No DOUBLE entry: if this authorization already has an OPEN entry (someone recorded them in
        // and they haven't been exited yet), reject — the visitor is already on-site. Record their
        // EXIT before a new entry. This stops a guard tapping "Record entry" twice from creating two
        // on-site rows for the same walk-in/pass (the reported bug). Re-entry is allowed once exited.
        var openExists = kind switch
        {
            AuthorizationKind.WalkIn => await db.EntryExitEvents.AsNoTracking()
                .AnyAsync(e => e.VisitorId == request.VisitorId && e.ExitTsUtc == null, cancellationToken),
            AuthorizationKind.VisitPass => await db.EntryExitEvents.AsNoTracking()
                .AnyAsync(e => e.VisitPassId == request.VisitPassId && e.ExitTsUtc == null, cancellationToken),
            _ => await db.EntryExitEvents.AsNoTracking()
                .AnyAsync(e => e.RecurringVisitorId == request.RecurringVisitorId && e.ExitTsUtc == null, cancellationToken),
        };
        if (openExists)
        {
            throw new ConflictException(
                "This visitor is already on-site (an entry is open). Record their exit before recording a new entry.");
        }

        // Watchlist override gate (Req 26.5): if the admitted subject (name / vehicle number) matches
        // the community's ACTIVE watchlist, the entry is FLAGGED and may only proceed when the caller
        // holds the Approve permission on Visitor (the authorized override). Without it the entry is
        // refused — FORBIDDEN, no event created — surfacing the match to the guard. The check is a
        // single indexed watchlist lookup (no scan).
        var entryMatch = await MatchWatchlistAnyAsync(
            admit.CommunityId,
            cancellationToken,
            admit.VisitorName,
            admit.VehicleNumber);
        if (entryMatch)
        {
            await audit.AppendAsync(
                new AuditEntry("WatchlistFlag", "EntryExitEvent", null, admit.CommunityId,
                    Detail: $"Subject '{admit.VisitorName}' matched an active watchlist entry at entry."),
                cancellationToken);

            if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Visitor", cancellationToken))
            {
                throw new ForbiddenException(
                    "The subject matches an active watchlist entry; an authorized override (Approve permission) is required to admit entry.");
            }
        }

        var evt = new EntryExitEvent
        {
            CommunityId = admit.CommunityId,
            GateId = request.GateId,
            GateLabel = string.IsNullOrWhiteSpace(request.GateLabel) ? null : request.GateLabel.Trim(),
            DestinationUnitId = admit.DestinationUnitId,
            VisitorId = request.VisitorId,
            VisitPassId = request.VisitPassId,
            RecurringVisitorId = request.RecurringVisitorId,
            VisitorName = admit.VisitorName,
            Category = admit.Category,
            VehicleNumber = string.IsNullOrWhiteSpace(request.VehicleNumber) ? admit.VehicleNumber : request.VehicleNumber.Trim(),
            EntryTsUtc = now,
            ExitTsUtc = null,
        };

        db.EntryExitEvents.Add(evt);

        await audit.AppendAsync(
            new AuditEntry("RecordEntry", "EntryExitEvent", evt.Id.ToString(), evt.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(evt);
    }

    public async Task<EntryExitEventDto> RecordExitAsync(RecordExitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EntryExitEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record an exit.");
        }

        var kind = RequireExactlyOneAuthorization(request.VisitorId, request.VisitPassId, request.RecurringVisitorId);

        // Match the OPEN event for the named authorization (Req 25.3). TRACKED load so the RowVersion
        // rides the UPDATE — two concurrent exits race on the row-version check and exactly one wins,
        // so a double-exit can't both stamp a time (the loser → CONFLICT centrally). The lookup is a
        // single indexed query on (CommunityId, <authorization id>) filtered to the open event. If the
        // authorization has more than one open event (shouldn't happen), close the earliest entry.
        var query = db.EntryExitEvents.Where(e => e.ExitTsUtc == null);
        query = kind switch
        {
            AuthorizationKind.WalkIn => query.Where(e => e.VisitorId == request.VisitorId),
            AuthorizationKind.VisitPass => query.Where(e => e.VisitPassId == request.VisitPassId),
            _ => query.Where(e => e.RecurringVisitorId == request.RecurringVisitorId),
        };

        var open = await query.OrderBy(e => e.EntryTsUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException(
                "openEvent",
                "No open entry event was found for this visitor; an exit must match an open entry.");

        authorizationGuard.EnsureCommunityAuthorized(open.CommunityId);

        open.ExitTsUtc = clock.UtcNow;
        if (request.GateId is { } gid)
        {
            open.GateId = gid;
        }

        if (!string.IsNullOrWhiteSpace(request.GateLabel))
        {
            open.GateLabel = request.GateLabel.Trim();
        }

        await audit.AppendAsync(
            new AuditEntry("RecordExit", "EntryExitEvent", open.Id.ToString(), open.CommunityId, Detail: null),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return ToDto(open);
    }

    public async Task<ScanAccessResult> ScanAccessAsync(ScanAccessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "EntryExitEvent", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to scan access at the gate.");
        }

        var qr = request.QrToken?.Trim();
        var otp = request.Otp?.Trim();
        if (string.IsNullOrEmpty(qr) && string.IsNullOrEmpty(otp))
        {
            throw new ValidationException("qrToken", "A QR token or OTP is required to scan access.");
        }

        var pass = await ResolveVisitPassForScanAsync(qr, otp, cancellationToken);
        if (pass is not null)
        {
            return await ScanVisitPassAsync(pass, request, cancellationToken);
        }

        var visitor = await ResolveVisitorForScanAsync(qr, otp, cancellationToken);
        if (visitor is not null)
        {
            return await ScanVisitorAsync(visitor, request, cancellationToken);
        }

        return new ScanAccessResult(ScanActions.Rejected, "unknown", string.Empty, "No active pass or approved visitor matches this code.");
    }

    private async Task<VisitPass?> ResolveVisitPassForScanAsync(string? qr, string? otp, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(qr))
        {
            return await db.VisitPasses.FirstOrDefaultAsync(p => p.QrToken == qr, cancellationToken);
        }

        var candidates = await db.VisitPasses
            .Where(p => p.Status == VisitPassStatuses.Active)
            .ToListAsync(cancellationToken);
        return candidates.FirstOrDefault(p => hasher.Verify(p.OtpHash, otp!));
    }

    private async Task<Visitor?> ResolveVisitorForScanAsync(string? qr, string? otp, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(qr))
        {
            return await db.Visitors.FirstOrDefaultAsync(
                v => v.QrToken == qr && v.Status == VisitorStatuses.Approved, cancellationToken);
        }

        var candidates = await db.Visitors
            .Where(v => v.Status == VisitorStatuses.Approved && v.OtpHash != null)
            .ToListAsync(cancellationToken);
        return candidates.FirstOrDefault(v => hasher.Verify(v.OtpHash!, otp!));
    }

    private async Task<ScanAccessResult> ScanVisitPassAsync(VisitPass pass, ScanAccessRequest request, CancellationToken cancellationToken)
    {
        authorizationGuard.EnsureCommunityAuthorized(pass.CommunityId);

        var hasOpen = await db.EntryExitEvents.AsNoTracking()
            .AnyAsync(e => e.VisitPassId == pass.Id && e.ExitTsUtc == null, cancellationToken);

        if (hasOpen)
        {
            await RecordExitAsync(new RecordExitRequest(null, pass.Id, null, request.GateId, request.GateLabel), cancellationToken);
            return new ScanAccessResult(ScanActions.CheckedOut, "visit_pass", pass.VisitorName, null);
        }

        var completedCycles = await db.EntryExitEvents.AsNoTracking()
            .CountAsync(e => e.VisitPassId == pass.Id && e.ExitTsUtc != null, cancellationToken);
        if (completedCycles >= pass.MaxUses || !string.Equals(pass.Status, VisitPassStatuses.Active, StringComparison.Ordinal))
        {
            return new ScanAccessResult(ScanActions.Closed, "visit_pass", pass.VisitorName, "This pass has no remaining access.");
        }

        await RecordEntryAsync(new RecordEntryRequest(null, pass.Id, null, request.GateId, request.GateLabel, null), cancellationToken);
        return new ScanAccessResult(ScanActions.CheckedIn, "visit_pass", pass.VisitorName, null);
    }

    private async Task<ScanAccessResult> ScanVisitorAsync(Visitor visitor, ScanAccessRequest request, CancellationToken cancellationToken)
    {
        authorizationGuard.EnsureCommunityAuthorized(visitor.CommunityId);

        var hasOpen = await db.EntryExitEvents.AsNoTracking()
            .AnyAsync(e => e.VisitorId == visitor.Id && e.ExitTsUtc == null, cancellationToken);

        if (hasOpen)
        {
            await RecordExitAsync(new RecordExitRequest(visitor.Id, null, null, request.GateId, request.GateLabel), cancellationToken);
            return new ScanAccessResult(ScanActions.CheckedOut, "visitor", visitor.Name, null);
        }

        var alreadyUsed = await db.EntryExitEvents.AsNoTracking()
            .AnyAsync(e => e.VisitorId == visitor.Id, cancellationToken);
        if (alreadyUsed)
        {
            return new ScanAccessResult(ScanActions.Closed, "visitor", visitor.Name, "This visitor has already checked in and out.");
        }

        await RecordEntryAsync(new RecordEntryRequest(visitor.Id, null, null, request.GateId, request.GateLabel, null), cancellationToken);
        return new ScanAccessResult(ScanActions.CheckedIn, "visitor", visitor.Name, null);
    }

    public async Task<VehicleLookupResult> LookupVehicleAsync(string plate, CancellationToken cancellationToken = default)
    {
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.View, "Vehicle", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to look up a vehicle.");
        }

        var normalized = NormalizePlate(plate);
        if (normalized.Length == 0)
        {
            throw new ValidationException("plate", "A vehicle number is required.");
        }

        var communityIds = tenant.AuthorizedCommunityIds.ToArray();
        var allCommunities = tenant.IsPlatformSuperAdmin;
        var matches = new List<VehicleLookupMatch>();

        var residentVehicles = await db.Vehicles.AsNoTracking().IgnoreQueryFilters()
            .Where(v => !v.IsDeleted && (allCommunities || communityIds.Contains(v.CommunityId))
                && v.RegistrationNumber.ToUpper().Replace(" ", "") == normalized)
            .Select(v => new { v.UnitId, v.VehicleType })
            .ToListAsync(cancellationToken);
        foreach (var v in residentVehicles)
        {
            var residents = await ResolveUnitResidentsAsync(v.UnitId, cancellationToken);
            var owner = residents.FirstOrDefault();
            matches.Add(new VehicleLookupMatch(
                VehicleMatchTypes.Resident, v.VehicleType,
                owner?.Name ?? "Resident vehicle", owner?.Phone, null,
                v.UnitId, await ResolveUnitNumberAsync(v.UnitId, cancellationToken), null, residents));
        }

        var passes = await db.VisitPasses.AsNoTracking().IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && (allCommunities || communityIds.Contains(p.CommunityId)) && p.VehicleNumber != null
                && p.VehicleNumber.ToUpper().Replace(" ", "") == normalized)
            .Select(p => new { p.VisitorName, p.Phone, p.Email, p.UnitId, p.VehicleType, p.Status })
            .ToListAsync(cancellationToken);
        foreach (var p in passes)
        {
            matches.Add(new VehicleLookupMatch(
                VehicleMatchTypes.VisitPass, p.VehicleType, p.VisitorName, p.Phone, p.Email,
                p.UnitId, await ResolveUnitNumberAsync(p.UnitId, cancellationToken), p.Status,
                await ResolveUnitResidentsAsync(p.UnitId, cancellationToken)));
        }

        var walkIns = await db.Visitors.AsNoTracking().IgnoreQueryFilters()
            .Where(v => !v.IsDeleted && (allCommunities || communityIds.Contains(v.CommunityId)) && v.VehicleNumber != null
                && v.VehicleNumber.ToUpper().Replace(" ", "") == normalized)
            .Select(v => new { v.Name, v.Phone, v.Email, v.UnitId, v.Status })
            .ToListAsync(cancellationToken);
        foreach (var v in walkIns)
        {
            matches.Add(new VehicleLookupMatch(
                VehicleMatchTypes.WalkIn, null, v.Name, v.Phone, v.Email,
                v.UnitId, await ResolveUnitNumberAsync(v.UnitId, cancellationToken), v.Status,
                await ResolveUnitResidentsAsync(v.UnitId, cancellationToken)));
        }

        return new VehicleLookupResult(normalized, matches);
    }

    private static string NormalizePlate(string? plate) =>
        (plate ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);

    private async Task<string?> ResolveUnitNumberAsync(Guid? unitId, CancellationToken cancellationToken)
    {
        if (unitId is not { } id)
        {
            return null;
        }
        return await db.Units.AsNoTracking().IgnoreQueryFilters()
            .Where(u => u.Id == id).Select(u => u.UnitNumber).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<VehicleLookupHostResident>> ResolveUnitResidentsAsync(Guid? unitId, CancellationToken cancellationToken)
    {
        if (unitId is not { } id)
        {
            return [];
        }
        return await db.HouseholdMembers.AsNoTracking().IgnoreQueryFilters()
            .Where(h => h.UnitId == id && h.EndDate == null && !h.IsDeleted)
            .Join(db.Residents.AsNoTracking().IgnoreQueryFilters().Where(r => !r.IsDeleted),
                h => h.ResidentId, r => r.Id,
                (h, r) => new VehicleLookupHostResident(r.Name, r.Phone, r.ResidentType))
            .ToListAsync(cancellationToken);
    }

    // =============================================================================================
    // Task 16.4 — paginated list/read surface (Req 23.5, 24.2, 25.4, 25.5)
    // =============================================================================================

    public async Task<PagedData<VisitPassDto>> ListVisitPassesAsync(ListQueryParameters parameters, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Tenant + resident self-scope filtered by the global query filter (Req 23.5): a resident
        // principal only sees passes for their own unit. OTP is never on VisitPassDto (secrets).
        var query = db.VisitPasses.AsNoTracking();
        if (organizationId is { } orgId)
        {
            var communityIds = await CommunityIdsForOrgAsync(orgId, cancellationToken);
            query = query.Where(p => communityIds.Contains(p.CommunityId));
        }

        var page = await query.ToPagedResultAsync(
            parameters, VisitPassQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
        var withOrg = await WithOrganizationNamesAsync(page.Items, p => p.CommunityId, (p, n) => p with { OrganizationName = n }, cancellationToken);
        return new PagedData<VisitPassDto> { Items = withOrg, Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount };
    }

    public async Task<VisitPassDto?> GetVisitPassAsync(Guid visitPassId, CancellationToken cancellationToken = default)
    {
        var pass = await db.VisitPasses.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visitPassId, cancellationToken);
        return pass is null ? null : ToDto(pass);
    }

    public async Task<PagedData<VisitorDto>> ListVisitorsAsync(ListQueryParameters parameters, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // The pending-approval queue (Req 24.2): tenant/self-scoped; a status=pending filter surfaces
        // the walk-ins a resident must approve/reject for their own unit.
        var query = db.Visitors.AsNoTracking();
        if (organizationId is { } orgId)
        {
            var communityIds = await CommunityIdsForOrgAsync(orgId, cancellationToken);
            query = query.Where(v => communityIds.Contains(v.CommunityId));
        }

        var page = await query.ToPagedResultAsync(
            parameters, VisitorQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
        var withUnits = await WithUnitNumbersAsync(page.Items, cancellationToken);
        var withOrg = await WithOrganizationNamesAsync(withUnits, v => v.CommunityId, (v, n) => v with { OrganizationName = n }, cancellationToken);
        return new PagedData<VisitorDto> { Items = withOrg, Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount };
    }

    private async Task<List<Guid>> CommunityIdsForOrgAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Communities.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

    // Resolve the organization NAME for a batch of DTOs in one join (no N+1), keyed by each row's
    // CommunityId → Community.OrganizationId → Organization.Name. IgnoreQueryFilters so the display
    // name is independent of the caller's tenant scope (mirrors WithUnitNumbersAsync).
    private async Task<IReadOnlyList<T>> WithOrganizationNamesAsync<T>(
        IReadOnlyList<T> items,
        Func<T, Guid> communityIdOf,
        Func<T, string, T> withName,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return items;
        var communityIds = items.Select(communityIdOf).Distinct().ToList();
        var orgNameByCommunity = await db.Communities.IgnoreQueryFilters().AsNoTracking()
            .Where(c => communityIds.Contains(c.Id))
            .Join(db.Organizations.IgnoreQueryFilters().AsNoTracking(),
                c => c.OrganizationId, o => o.Id,
                (c, o) => new { c.Id, OrgName = o.Name })
            .ToDictionaryAsync(x => x.Id, x => x.OrgName, cancellationToken);
        return items
            .Select(i => orgNameByCommunity.TryGetValue(communityIdOf(i), out var n) ? withName(i, n) : i)
            .ToList();
    }

    public async Task<VisitorDto?> GetVisitorAsync(Guid visitorId, CancellationToken cancellationToken = default)
    {
        var visitor = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitorId, cancellationToken);
        if (visitor is null) return null;
        var filled = await WithUnitNumbersAsync(new[] { ToDto(visitor) }, cancellationToken);
        return filled[0];
    }

    // Resolve the host unit number for a batch of visitor DTOs in one query (avoids N+1) and returns
    // new DTOs carrying HostUnitNumber. Tenant-scoped by the global filter.
    private async Task<IReadOnlyList<VisitorDto>> WithUnitNumbersAsync(IReadOnlyList<VisitorDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return items;
        var unitIds = items.Select(i => i.HostUnitId).Distinct().ToList();
        var numbers = await db.Units.IgnoreQueryFilters().AsNoTracking()
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.UnitNumber, cancellationToken);
        return items
            .Select(i => numbers.TryGetValue(i.HostUnitId, out var n) ? i with { HostUnitNumber = n } : i)
            .ToList();
    }

    public async Task<PagedData<EntryExitEventDto>> ListEntryExitEventsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var page = await db.EntryExitEvents.AsNoTracking().ToPagedResultAsync(
            parameters, EntryExitEventQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
        var enriched = await WithAuthorizationDetailAsync(page.Items, cancellationToken);
        return new PagedData<EntryExitEventDto> { Items = enriched, Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount };
    }

    private async Task<IReadOnlyList<EntryExitEventDto>> WithAuthorizationDetailAsync(IReadOnlyList<EntryExitEventDto> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return items;

        var visitorIds = items.Where(e => e.VisitorId is not null).Select(e => e.VisitorId!.Value).Distinct().ToList();
        var passIds = items.Where(e => e.VisitPassId is not null).Select(e => e.VisitPassId!.Value).Distinct().ToList();

        var visitorDetail = visitorIds.Count == 0
            ? new Dictionary<Guid, (Guid? HostUnitId, string? Phone, Guid? PhotoFileId, string? WhomToMeet)>()
            : await db.Visitors.IgnoreQueryFilters().AsNoTracking()
                .Where(v => visitorIds.Contains(v.Id))
                .Select(v => new { v.Id, v.UnitId, v.Phone, v.PhotoFileId, v.WhomToMeet })
                .ToDictionaryAsync(v => v.Id, v => (HostUnitId: v.UnitId, Phone: v.Phone, PhotoFileId: v.PhotoFileId, WhomToMeet: v.WhomToMeet), cancellationToken);

        var passDetail = passIds.Count == 0
            ? new Dictionary<Guid, (Guid? HostUnitId, string? Phone, Guid? PhotoFileId, string? WhomToMeet)>()
            : await db.VisitPasses.IgnoreQueryFilters().AsNoTracking()
                .Where(p => passIds.Contains(p.Id))
                .Select(p => new { p.Id, p.UnitId, p.Phone, p.PhotoFileId, p.WhomToMeet })
                .ToDictionaryAsync(p => p.Id, p => (HostUnitId: p.UnitId, Phone: p.Phone, PhotoFileId: p.PhotoFileId, WhomToMeet: p.WhomToMeet), cancellationToken);

        var unitIds = visitorDetail.Values.Select(d => d.HostUnitId)
            .Concat(passDetail.Values.Select(d => d.HostUnitId))
            .Where(id => id is not null).Select(id => id!.Value)
            .Distinct().ToList();
        var unitNumbers = unitIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Units.IgnoreQueryFilters().AsNoTracking()
                .Where(u => unitIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.UnitNumber, cancellationToken);

        return items.Select(e =>
        {
            (Guid? HostUnitId, string? Phone, Guid? PhotoFileId, string? WhomToMeet)? d = null;
            if (e.VisitorId is { } vid && visitorDetail.TryGetValue(vid, out var vd)) d = vd;
            else if (e.VisitPassId is { } pid && passDetail.TryGetValue(pid, out var pd)) d = pd;
            if (d is null) return e;
            var unitNo = d.Value.HostUnitId is { } uid && unitNumbers.TryGetValue(uid, out var n) ? n : null;
            return e with { HostUnitNumber = unitNo, VisitorPhone = d.Value.Phone, PhotoFileId = d.Value.PhotoFileId, WhomToMeet = d.Value.WhomToMeet };
        }).ToList();
    }

    public Task<PagedData<RecurringVisitorDto>> ListRecurringVisitorsAsync(ListQueryParameters parameters, bool? active = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var query = db.RecurringVisitors.AsNoTracking();
        if (active is { } wantActive)
        {
            var today = clock.UtcNow.Date;
            query = wantActive
                ? query.Where(r => r.Status == RecurringVisitorStatuses.Active && r.EndDate >= today)
                : query.Where(r => r.Status != RecurringVisitorStatuses.Active || r.EndDate < today);
        }

        return query.ToPagedResultAsync(
            parameters, RecurringVisitorQueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<RecurringVisitorDto?> GetRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default)
    {
        var recurring = await db.RecurringVisitors.AsNoTracking().FirstOrDefaultAsync(r => r.Id == recurringVisitorId, cancellationToken);
        return recurring is null ? null : ToDto(recurring);
    }

    /// <summary>
    /// Shared pending → approved/rejected transition for a walk-in (Req 24.3, 24.4). Enforces the
    /// Approve permission + authorized community, that the walk-in is still pending (an invalid
    /// transition → CONFLICT — also what the concurrency loser observes), stamps the decision
    /// (resident + timestamp), audits it, and commits under the visitor RowVersion so the decision
    /// records exactly once.
    /// </summary>
    private async Task<VisitorDto> DecideWalkInAsync(
        Guid visitorId,
        Guid decidingResidentId,
        string toStatus,
        string auditAction,
        string? detail,
        CancellationToken cancellationToken)
    {
        // Defense-in-depth (Req 24.3): a resident approves/rejects with Approve on Visitor.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Visitor", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to approve or reject a walk-in visitor.");
        }

        // TRACKED load: we mutate the row and need the RowVersion carried into the UPDATE. The global
        // tenant + resident self-scope filter restricts this to the caller's own unit, so a
        // cross-unit/foreign-tenant walk-in is a not-found.
        var visitor = await db.Visitors.FirstOrDefaultAsync(v => v.Id == visitorId, cancellationToken)
            ?? throw new NotFoundException("Visitor not found.");

        authorizationGuard.EnsureCommunityAuthorized(visitor.CommunityId);

        // Only a pending walk-in can be decided (Req 24.5). An approve/reject against a non-pending
        // one conflicts with the current state (CONFLICT) — the same thing the concurrency loser sees
        // after the winner moved the row.
        if (!string.Equals(visitor.Status, VisitorStatuses.Pending, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"The visitor is '{visitor.Status}', not 'pending'; the decision is not valid for its current status.");
        }

        visitor.Status = toStatus;
        visitor.ApprovedByResidentId = decidingResidentId;
        visitor.ApprovalDecisionAtUtc = clock.UtcNow;

        await audit.AppendAsync(
            new AuditEntry(auditAction, "Visitor", visitor.Id.ToString(), visitor.CommunityId, Detail: detail),
            cancellationToken);

        // Notify the GUARD who registered this walk-in of the decision (Task 20261008), so their
        // in-app inbox shows whether it was approved/rejected. Uses the SAME common EnqueueAsync
        // helper (only Adds the outbox row — committed atomically by the SaveChanges below). The
        // guard is Visitor.CreatedBy (stamped by the audit interceptor at registration). Best-effort:
        // a missing/unparseable creator just skips the notice and never blocks the decision.
        await NotifyRegisteringGuardAsync(visitor, toStatus, detail, cancellationToken);

        string? approvalOtp = null;
        if (string.Equals(toStatus, VisitorStatuses.Approved, StringComparison.Ordinal))
        {
            approvalOtp = GenerateOtp();
            visitor.OtpHash = hasher.Hash(approvalOtp);
            visitor.QrToken = await GenerateUniqueVisitorQrTokenAsync(visitor.CommunityId, cancellationToken);
            await NotifyVisitorOfApprovalAsync(visitor, approvalOtp, cancellationToken);
        }

        // RowVersion makes the decision record exactly once under concurrency: the loser's save trips
        // a DbUpdateConcurrencyException mapped centrally to CONFLICT (Req 24.3).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(visitor);
    }

    /// <summary>
    /// Enqueue the walk-in decision notice to the guard who registered it (Task 20261008). The guard
    /// is <see cref="Visitor.CreatedBy"/>; resolve the host unit's number for the message. Enqueue
    /// only Adds the outbox row (no save) so it commits with the decision. No recipient / unknown
    /// template → silent no-op (the enqueue helper never throws), so the decision is never blocked.
    /// </summary>
    private async Task NotifyRegisteringGuardAsync(Visitor visitor, string toStatus, string? reason, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(visitor.CreatedBy, out var guardUserId))
        {
            return;
        }

        var unitNumber = await db.Units.AsNoTracking()
            .Where(u => u.Id == visitor.HostUnitId)
            .Select(u => u.UnitNumber)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var approved = string.Equals(toStatus, VisitorStatuses.Approved, StringComparison.Ordinal);
        var placeholders = new Dictionary<string, string>
        {
            ["visitorName"] = visitor.Name,
            ["unitNumber"] = unitNumber,
            ["reason"] = string.IsNullOrWhiteSpace(reason) ? string.Empty : $"Reason: {reason}.",
        };

        await notifications.EnqueueAsync(
            new NotificationRequest(
                approved ? NotificationTemplateKeys.VisitorApproved : NotificationTemplateKeys.VisitorRejected,
                guardUserId,
                visitor.CommunityId,
                Placeholders: placeholders),
            cancellationToken);
    }

    public async Task<int> ExpirePendingWalkInsAsync(int maxVisitors, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(maxVisitors, 1, MaxPendingExpirySweepBatch);
        var now = clock.UtcNow;

        var candidates = await db.Visitors
            .Where(v => v.Status == VisitorStatuses.Pending)
            .OrderBy(v => v.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var expiryMinutesByCommunity = new Dictionary<Guid, int>();
        foreach (var communityId in candidates.Select(v => v.CommunityId).Distinct())
        {
            expiryMinutesByCommunity[communityId] = await config.GetIntAsync(
                WalkInPendingExpiryMinutesKey, DefaultWalkInPendingExpiryMinutes, communityId, cancellationToken);
        }

        var expired = 0;
        foreach (var visitor in candidates)
        {
            var expiryMinutes = expiryMinutesByCommunity[visitor.CommunityId];
            if (expiryMinutes <= 0)
            {
                continue;
            }

            var minutesPending = (now - visitor.CreatedAtUtc).TotalMinutes;
            if (minutesPending < expiryMinutes)
            {
                continue;
            }

            visitor.Status = VisitorStatuses.Expired;
            visitor.ApprovalDecisionAtUtc = now;

            await audit.AppendAsync(
                new AuditEntry("Expire", "Visitor", visitor.Id.ToString(), visitor.CommunityId,
                    Detail: $"pendingMinutes={minutesPending:F1}; expiryMinutes={expiryMinutes}"),
                cancellationToken);

            await NotifyRegisteringGuardOfExpiryAsync(visitor, cancellationToken);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                expired++;
            }
            catch (ConflictException)
            {
                db.Entry(visitor).State = EntityState.Detached;
            }
        }

        return expired;
    }

    private async Task NotifyRegisteringGuardOfExpiryAsync(Visitor visitor, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(visitor.CreatedBy, out var guardUserId))
        {
            return;
        }

        var unitNumber = await db.Units.AsNoTracking()
            .Where(u => u.Id == visitor.HostUnitId)
            .Select(u => u.UnitNumber)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var placeholders = new Dictionary<string, string>
        {
            ["visitorName"] = visitor.Name,
            ["unitNumber"] = unitNumber,
        };

        await notifications.EnqueueAsync(
            new NotificationRequest(
                NotificationTemplateKeys.VisitorExpired,
                guardUserId,
                visitor.CommunityId,
                Placeholders: placeholders),
            cancellationToken);
    }

    private async Task NotifyVisitorOfApprovalAsync(Visitor visitor, string otp, CancellationToken cancellationToken)
    {
        var token = visitor.QrToken ?? string.Empty;
        var smsText = $"Hello {visitor.Name}, your visit is approved. Show this at the gate. Code (OTP): {otp}. Pass: {token}.";
        var emailSubject = "Your visit is approved";
        var emailBody = $"Hello {visitor.Name},\n\nYour visit has been approved by the resident. Please show this at the gate.\n\nOne-time code (OTP): {otp}\nPass token (QR): {token}\n\nThank you.";

        if (!string.IsNullOrWhiteSpace(visitor.Phone))
        {
            try
            {
                await messages.QueueSmsAsync(visitor.Phone, smsText, cancellationToken);
            }
            catch (ValidationException)
            {
            }
        }

        if (smtpOptions.Value.IsEnabled && !string.IsNullOrWhiteSpace(visitor.Email))
        {
            try
            {
                await messages.QueueEmailAsync(visitor.Email, emailSubject, emailBody, cancellationToken);
            }
            catch (ValidationException)
            {
            }
        }
    }

    /// <summary>
    /// Notify the residents of the walk-in's host unit (Req 24.2). Resolves the unit's current
    /// household residents that have a linked login user (a notification needs a recipient user) in a
    /// SINGLE tenant-scoped read (no N+1), and enqueues the visitor-request template for each. The
    /// enqueue only Adds a Pending outbox row to the current DbContext — it does not save — so the
    /// caller's SaveChangesAsync commits the walk-in + the notifications atomically.
    /// </summary>
    private async Task NotifyHostUnitResidentsAsync(Visitor visitor, CancellationToken cancellationToken)
    {
        // Current (open) household members of the host unit → their linked login user ids. One
        // projecting join, distinct, no N+1.
        var recipientUserIds = await db.HouseholdMembers.AsNoTracking()
            .Where(h => h.UnitId == visitor.HostUnitId && h.EndDate == null)
            .Join(db.Residents.AsNoTracking().Where(r => r.UserId != null),
                h => h.ResidentId,
                r => r.Id,
                (h, r) => r.UserId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (recipientUserIds.Count == 0)
        {
            return;
        }

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["visitorName"] = visitor.Name,
            ["category"] = visitor.Category,
        };

        foreach (var userId in recipientUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(
                    NotificationTemplateKeys.VisitorRequest,
                    userId,
                    visitor.CommunityId,
                    Placeholders: placeholders),
                cancellationToken);
        }
    }

    /// <summary>
    /// Resolve the host unit (tenant-scoped) and return its community id, rejecting an unknown /
    /// foreign-tenant / (for a resident) non-own unit. The returned community anchors the pass/visitor
    /// so it is always owned by the same community as its host unit (same-community by construction,
    /// Req 23.1, 24.1).
    /// </summary>
    private async Task<Guid> RequireHostUnitCommunityAsync(Guid hostUnitId, CancellationToken cancellationToken)
    {
        var communityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == hostUnitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new ValidationException("hostUnitId", "The host unit was not found in your authorized communities.");
    }

    private async Task RequireValidCategoryAsync(string? category, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ValidationException("category", "A visitor category is required.");
        }

        // Validate against the configurable Visitor_Category list resolved for this community
        // (Req 24.1, 25.1) — not a frozen enum, so categories stay editable per community.
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.VisitorCategory, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, category.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("category", $"'{category}' is not a valid visitor category.");
        }
    }

    private async Task RequireValidIdProofTypeAsync(string? idProofType, Guid communityId, CancellationToken cancellationToken)
    {
        // Optional field: only validate when supplied. Captured for future reference against the
        // editable ID_Proof_Type list (steering: no hardcoding), not a frozen enum.
        if (string.IsNullOrWhiteSpace(idProofType))
        {
            return;
        }

        var allowed = await config.GetMasterDataAsync(MasterDataKeys.IdProofType, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, idProofType.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("idProofType", $"'{idProofType}' is not a valid ID-proof type.");
        }
    }

    /// <summary>
    /// Add the pass with a freshly generated opaque QR token, letting the DB unique index
    /// (CommunityId, QrToken) decide the winner. On the vanishingly unlikely collision, regenerate
    /// and retry (bounded) rather than a read-then-write pre-check — the same "let the DB decide"
    /// stance as the Vehicle registration-number index.
    /// </summary>
    private async Task SaveWithUniqueQrTokenAsync(VisitPass pass, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            pass.QrToken = GenerateQrToken();
            if (string.IsNullOrEmpty(pass.PublicToken) || attempt > 1)
            {
                pass.PublicToken = GenerateQrToken();
            }
            db.VisitPasses.Add(pass);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (attempt < QrTokenAttempts)
            {
                // Collision (or another unique violation on this insert): detach and retry with a new
                // token. Bounded so a persistent failure still surfaces.
                db.Entry(pass).State = EntityState.Detached;
            }
        }
    }

    public async Task<VisitPassValidity> ResolveDefaultValidityAsync(Guid? communityId, CancellationToken cancellationToken = default)
    {
        var amount = await config.GetIntAsync(DefaultValidityKey, 0, communityId, cancellationToken);
        if (amount >= 1)
        {
            var unitRaw = await config.GetStringAsync(DefaultValidityUnitKey, "days", communityId, cancellationToken);
            var unit = string.Equals(unitRaw.Trim(), "hours", StringComparison.OrdinalIgnoreCase)
                ? VisitPassValidityUnit.Hours
                : VisitPassValidityUnit.Days;
            return new VisitPassValidity(amount, unit);
        }

        var legacyHours = await config.GetIntAsync(DefaultValidityHoursKey, DefaultValidityHours, communityId, cancellationToken);
        return new VisitPassValidity(Math.Max(1, legacyHours), VisitPassValidityUnit.Hours);
    }

    private VerifyVisitPassResult Reject(string reason) =>
        new(Accepted: false, RejectionReason: reason, Pass: null);

    /// <summary>
    /// Whether ANY of the supplied subject identifiers (name / phone / vehicle number) matches the
    /// community's ACTIVE watchlist (Req 26.5). Delegates to <see cref="ISecurityOpsService"/>'s
    /// indexed match (normalized equality on <c>(CommunityId, SubjectIdentifier)</c>); null/blank
    /// identifiers are skipped. Short-circuits on the first match.
    /// </summary>
    private async Task<bool> MatchWatchlistAnyAsync(Guid communityId, CancellationToken cancellationToken, params string?[] identifiers)
    {
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                continue;
            }

            var matches = await securityOps.MatchWatchlistAsync(communityId, identifier, cancellationToken);
            if (matches.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateQrToken() =>
        // 256 bits of entropy, URL-safe, opaque — reveals nothing about the pass.
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private async Task<string> GenerateUniqueVisitorQrTokenAsync(Guid communityId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = GenerateQrToken();
            var exists = await db.Visitors.IgnoreQueryFilters()
                .AnyAsync(v => v.CommunityId == communityId && v.QrToken == token, cancellationToken);
            if (!exists || attempt >= QrTokenAttempts)
            {
                return token;
            }
        }
    }

    private static string GenerateOtp() =>
        // 6-digit numeric OTP (000000–999999), unbiased.
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }

        return value.Trim();
    }

    private static VisitPassDto ToDto(VisitPass p) =>
        new(p.Id, p.CommunityId, p.HostUnitId, p.HostResidentId, p.VisitorName, p.Phone, p.Email,
            p.VehicleNumber, p.VehicleType, p.NumberOfPersons, p.WhomToMeet, p.VisitDateUtc, p.PhotoFileId,
            p.QrToken, p.ValidFromUtc, p.ValidUntilUtc, p.MaxUses, p.UseCount, p.Status,
            PublicToken: p.PublicToken);

    private static VisitorDto ToDto(Visitor v) =>
        new(v.Id, v.CommunityId, v.HostUnitId, v.Name, v.Phone, v.Email, v.Category, v.PhotoFileId,
            v.VehicleNumber, v.GateLocation, v.IdProofType, v.IdProofNumber, v.Purpose,
            v.WhomToMeet, v.ExpectedDurationMinutes, v.Status, v.ApprovedByResidentId,
            v.ApprovalDecisionAtUtc, v.IsAdmittable, v.QrToken);

    private static RecurringVisitorDto ToDto(RecurringVisitor r) =>
        new(r.Id, r.CommunityId, r.HostUnitId, r.HostResidentId, r.Name, r.Phone, r.Category,
            r.DaysOfWeek, r.StartDate, r.EndDate, r.DailyStartMinutes, r.DailyEndMinutes, r.Status, r.PhotoFileId);

    private static EntryExitEventDto ToDto(EntryExitEvent e) =>
        new(e.Id, e.CommunityId, e.GateId, e.GateLabel, e.DestinationUnitId, e.VisitorId,
            e.VisitPassId, e.RecurringVisitorId, e.VisitorName, e.Category, e.VehicleNumber,
            e.EntryTsUtc, e.ExitTsUtc);

    /// <summary>Which authorization an entry/exit request names (exactly one).</summary>
    private enum AuthorizationKind
    {
        WalkIn,
        VisitPass,
        Recurring,
    }

    /// <summary>
    /// The resolved, admittable authorization behind an entry (Req 25.3): the owning community and the
    /// denormalized details captured onto the event.
    /// </summary>
    private sealed record AdmittableAuthorization(
        Guid CommunityId,
        string VisitorName,
        string Category,
        string? VehicleNumber,
        Guid? DestinationUnitId);

    /// <summary>
    /// Require that an entry/exit request names EXACTLY ONE authorization (walk-in / pass / recurring)
    /// — zero or more than one is a validation error — and return which one.
    /// </summary>
    private static AuthorizationKind RequireExactlyOneAuthorization(Guid? visitorId, Guid? visitPassId, Guid? recurringVisitorId)
    {
        var count = (visitorId is not null ? 1 : 0) + (visitPassId is not null ? 1 : 0) + (recurringVisitorId is not null ? 1 : 0);
        if (count != 1)
        {
            throw new ValidationException(
                "authorization",
                "Exactly one of visitorId, visitPassId or recurringVisitorId must identify the admitting authorization.");
        }

        if (visitorId is not null)
        {
            return AuthorizationKind.WalkIn;
        }

        return visitPassId is not null ? AuthorizationKind.VisitPass : AuthorizationKind.Recurring;
    }

    /// <summary>
    /// Resolve the named authorization and ENFORCE admittability for an entry (Req 24.4, 25.3). A
    /// walk-in must be <c>approved</c> (<see cref="Visitor.IsAdmittable"/> — a pending/rejected one is
    /// rejected); a pass must be active, inside its validity window and have uses remaining; a
    /// recurring visitor must be authorized for <paramref name="whenUtc"/>
    /// (<see cref="RecurringVisitor.IsAuthorizedOn"/>). A missing/foreign-tenant authorization (hidden
    /// by the global filter) or a non-admittable one throws a ValidationException so NO entry event is
    /// created. Reads are tenant-scoped <c>AsNoTracking</c> single indexed lookups (no N+1).
    /// </summary>
    private async Task<AdmittableAuthorization> ResolveAdmittableAuthorizationAsync(
        AuthorizationKind kind,
        Guid? visitorId,
        Guid? visitPassId,
        Guid? recurringVisitorId,
        DateTime whenUtc,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case AuthorizationKind.WalkIn:
            {
                var v = await db.Visitors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == visitorId, cancellationToken)
                    ?? throw new ValidationException("visitorId", "The visitor was not found in your authorized communities.");
                if (!v.IsAdmittable)
                {
                    // Req 24.4 / 25.3: a pending or rejected walk-in is NOT admittable — no entry event.
                    throw new ValidationException("visitorId", $"The visitor is '{v.Status}' and cannot be admitted.");
                }

                return new AdmittableAuthorization(v.CommunityId, v.Name, v.Category, v.VehicleNumber, v.UnitId);
            }

            case AuthorizationKind.VisitPass:
            {
                var p = await db.VisitPasses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == visitPassId, cancellationToken)
                    ?? throw new ValidationException("visitPassId", "The visit pass was not found in your authorized communities.");
                if (!IsPassAdmittable(p, whenUtc))
                {
                    throw new ValidationException("visitPassId", "The visit pass is not valid for entry (expired, used or revoked).");
                }

                return new AdmittableAuthorization(p.CommunityId, p.VisitorName, Category: "guest", VehicleNumber: null, p.UnitId);
            }

            default:
            {
                var r = await db.RecurringVisitors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == recurringVisitorId, cancellationToken)
                    ?? throw new ValidationException("recurringVisitorId", "The recurring visitor was not found in your authorized communities.");
                if (!r.IsAuthorizedOn(whenUtc))
                {
                    // Req 25.2 / 25.3: outside the schedule or revoked → not authorized for entry now.
                    throw new ValidationException("recurringVisitorId", "The recurring visitor is not authorized to enter at this time.");
                }

                return new AdmittableAuthorization(r.CommunityId, r.Name, r.Category, VehicleNumber: null, r.UnitId);
            }
        }
    }

    /// <summary>
    /// Whether a pass is valid for a gate entry right now (Req 23.4 rule reused for entry capture):
    /// active, within its validity window and with uses remaining. Mirrors the verify-time acceptance
    /// test; entry capture is a read-only admittability check (it does not itself consume a use —
    /// gate verification does that).
    /// </summary>
    private static bool IsPassAdmittable(VisitPass p, DateTime whenUtc) =>
        string.Equals(p.Status, VisitPassStatuses.Active, StringComparison.Ordinal)
        && whenUtc >= p.ValidFromUtc
        && whenUtc <= p.ValidUntilUtc
        && p.UseCount < p.MaxUses;

    /// <summary>
    /// Validate a recurring-visitor schedule (Req 25.2): at least one authorized day, a valid date
    /// range (end on/after start), and — when a daily window is supplied — well-formed, in-bounds
    /// minutes with start before end.
    /// </summary>
    private static void ValidateSchedule(CreateRecurringVisitorRequest request)
    {
        if (request.DaysOfWeek is <= 0 or > 0b1111111)
        {
            throw new ValidationException("daysOfWeek", "At least one valid day-of-week must be selected.");
        }

        if (request.EndDate.Date < request.StartDate.Date)
        {
            throw new ValidationException("endDate", "The end date must be on or after the start date.");
        }

        var hasStart = request.DailyStartMinutes is not null;
        var hasEnd = request.DailyEndMinutes is not null;
        if (hasStart != hasEnd)
        {
            throw new ValidationException("dailyWindow", "A daily time window needs both a start and an end, or neither.");
        }

        if (hasStart)
        {
            var start = request.DailyStartMinutes!.Value;
            var end = request.DailyEndMinutes!.Value;
            if (start is < 0 or > 1439 || end is < 1 or > 1440 || start >= end)
            {
                throw new ValidationException("dailyWindow", "The daily time window must be within a day and start before it ends.");
            }
        }
    }
}
