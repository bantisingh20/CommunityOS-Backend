using App.Application.Querying;
using App.Contracts.Envelope;

namespace App.Application.Gate;


public interface IGateService
{

    Task<CreateVisitPassResult> CreateVisitPassAsync(CreateVisitPassRequest request, CancellationToken cancellationToken = default);

    /// <summary>Anonymous public view of a pass by its opaque PublicToken (no tenant context). Null when not found.</summary>
    Task<PublicVisitPassDto?> GetPublicPassAsync(string publicToken, CancellationToken cancellationToken = default);

    /// <summary>Admin-triggered share: enqueue the public pass link over the requested channels (SMS/WhatsApp/email).</summary>
    Task<SharePassResult> SharePassAsync(Guid passId, SharePassRequest request, CancellationToken cancellationToken = default);

    Task<VisitPassValidity> ResolveDefaultValidityAsync(Guid? communityId, CancellationToken cancellationToken = default);
    
    Task<VerifyVisitPassResult> VerifyVisitPassAsync(VerifyVisitPassRequest request, CancellationToken cancellationToken = default);
   
    Task<RegisterWalkInResult> RegisterWalkInAsync(RegisterWalkInRequest request, CancellationToken cancellationToken = default);
    
    Task<VisitorDto> ApproveWalkInAsync(Guid visitorId, Guid approvingResidentId, CancellationToken cancellationToken = default);

    Task<VisitorDto> RejectWalkInAsync(Guid visitorId, Guid rejectingResidentId, RejectWalkInRequest request, CancellationToken cancellationToken = default);

    Task<int> ExpirePendingWalkInsAsync(int maxVisitors, CancellationToken cancellationToken = default);

    Task<RecurringVisitorDto> CreateRecurringVisitorAsync(CreateRecurringVisitorRequest request, CancellationToken cancellationToken = default);

    Task<RecurringVisitorDto> RevokeRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default);

    Task<RecurringVisitorDto> ExtendRecurringVisitorAsync(Guid recurringVisitorId, DateTime newEndDateUtc, CancellationToken cancellationToken = default);

    Task<int> GetRecurringExpiringSoonDaysAsync(Guid? communityId, CancellationToken cancellationToken = default);

    Task<bool> IsRecurringAuthorizedAsync(Guid recurringVisitorId, DateTime whenUtc, CancellationToken cancellationToken = default);

    Task<EntryExitEventDto> RecordEntryAsync(RecordEntryRequest request, CancellationToken cancellationToken = default);
    
    Task<EntryExitEventDto> RecordExitAsync(RecordExitRequest request, CancellationToken cancellationToken = default);

    Task<ScanAccessResult> ScanAccessAsync(ScanAccessRequest request, CancellationToken cancellationToken = default);

    Task<VehicleLookupResult> LookupVehicleAsync(string plate, CancellationToken cancellationToken = default);
   
    Task<PagedData<VisitPassDto>> ListVisitPassesAsync(ListQueryParameters parameters, Guid? organizationId = null, CancellationToken cancellationToken = default);

    Task<VisitPassDto?> GetVisitPassAsync(Guid visitPassId, CancellationToken cancellationToken = default);

    Task<PagedData<VisitorDto>> ListVisitorsAsync(ListQueryParameters parameters, Guid? organizationId = null, CancellationToken cancellationToken = default);

    Task<VisitorDto?> GetVisitorAsync(Guid visitorId, CancellationToken cancellationToken = default);

    Task<PagedData<EntryExitEventDto>> ListEntryExitEventsAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default);
 
    Task<PagedData<RecurringVisitorDto>> ListRecurringVisitorsAsync(ListQueryParameters parameters, bool? active = null, CancellationToken cancellationToken = default);

    Task<RecurringVisitorDto?> GetRecurringVisitorAsync(Guid recurringVisitorId, CancellationToken cancellationToken = default);
}
