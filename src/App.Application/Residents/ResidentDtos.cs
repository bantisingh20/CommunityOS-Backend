namespace App.Application.Residents;


public sealed record ResidentDto(
    Guid Id,
    Guid CommunityId,
    Guid? UserId,
    string Name,
    string? Email,
    string? Phone,
    string ResidentType,
    string VerificationStatus,
    string? RejectionReason,
    bool IsArchived,
    string? PhotoFileId = null,
    DateOnly? DateOfBirth = null,
    string? Gender = null,
    DateOnly? MoveInDate = null,
    string? GovtIdType = null,
    string? GovtIdFrontFileId = null,
    string? GovtIdBackFileId = null,
    string? LoginStatus = null);


public sealed record HouseholdMemberDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    Guid ResidentId,
    string Relationship,
    DateTime StartDate,
    DateTime? EndDate);


public sealed record UnitHouseholdMemberDto(
    Guid ResidentId,
    string Name,
    string ResidentType,
    string Relationship,
    DateTime StartDate);


public sealed record ResidentHouseholdUnitDto(
    Guid UnitId,
    string UnitNumber,
    string UnitType,
    string UnitStatus,
    string? Floor,
    Guid CommunityId,
    string Relationship,
    DateTime StartDate);


public sealed record ResidentMeDto(
    ResidentDto? Resident,
    IReadOnlyList<ResidentHouseholdUnitDto> Units);


public sealed record EmergencyContactDto(
    Guid Id,
    Guid ResidentId,
    string Name,
    string Phone,
    string Relationship);


public sealed record CreateResidentRequest(
    Guid CommunityId,
    Guid? UserId,
    string Name,
    string? Email,
    string? Phone,
    string ResidentType,
    string? PhotoFileId = null);

/// <summary>The signed-in resident's own KYC submission (draft/rejected → pending). All optional.</summary>
public sealed record SubmitKycRequest(
    string? ResidentType = null,
    DateOnly? DateOfBirth = null,
    string? Gender = null,
    DateOnly? MoveInDate = null,
    string? GovtIdType = null,
    string? SelfieBase64 = null,
    string? GovtIdFrontBase64 = null,
    string? GovtIdBackBase64 = null);

public sealed record UpdateResidentRequest(
    string? Name,
    string? ResidentType,
    string? Email,
    bool UpdateEmail,
    string? Phone,
    bool UpdatePhone,
    Guid? UserId,
    bool UpdateUserId,
    string? PhotoFileId = null,
    bool UpdatePhotoFileId = false);


public sealed record AssociateHouseholdRequest(
    Guid UnitId,
    string Relationship,
    DateTime? StartDate);


public sealed record AddEmergencyContactRequest(
    string Name,
    string Phone,
    string Relationship);

public sealed record CreateResidentLoginRequest(
    string Identifier,
    string Password,
    string? RoleName = null);

public sealed record ResidentLoginResult(
    Guid UserId,
    string Identifier,
    string? RoleAssigned);


public sealed record RejectResidentRequest(string Reason);


public sealed record MoveInRequest(
    Guid UnitId,
    DateTime? MoveInDate,
    string? Relationship);

public sealed record MoveOutRequest(
    Guid UnitId,
    DateTime? MoveOutDate);
