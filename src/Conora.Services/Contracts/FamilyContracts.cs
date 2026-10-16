using Conora.Domain.Enums;

namespace Conora.Services.Contracts;

public sealed record FamilyMemberUserResponse(
    Guid UsuarioId,
    string Name,
    string Email,
    bool IsSelf,
    bool IsBudgetOwner);

public sealed record FamilyInviteRequest(string Email);

public sealed record FamilyInviteResponse(
    Guid Id,
    string Email,
    DateTime ExpiresAt,
    DateTime? AcceptedAt,
    DateTime? CancelledAt,
    bool IsOpen);

public sealed record FamilyQrInviteResponse(
    Guid Id,
    string Token,
    DateTime ExpiresAt,
    string JoinUrl);

public sealed record FamilyInvitePreviewResponse(
    string Status,
    string InviterName,
    string Email,
    DateTime? ExpiresAt);

public sealed record FamilyNoticeResponse(Guid Id, string Kind, string Message, DateTime CreatedAt, bool IsRead);

public sealed record FamilyOwnerTransferResponse(
    Guid Id,
    Guid FromUsuarioId,
    string FromName,
    Guid ToUsuarioId,
    string ToName,
    DateTime ExpiresAt,
    bool IsOpen,
    bool IsIncoming);

public sealed record FamilyOwnerTransferRequest(Guid ToUsuarioId);

public sealed record FamilyGroupResponse(
    Guid? GroupId,
    Guid? BudgetOwnerUsuarioId,
    bool IsBudgetOwner,
    bool CanLeave,
    IReadOnlyList<FamilyMemberUserResponse> Members,
    IReadOnlyList<FamilyInviteResponse> PendingInvites,
    IReadOnlyList<FamilyNoticeResponse> Notices,
    FamilyOwnerTransferResponse? PendingOwnerTransfer);

public sealed record UpdateProfileRequest(string Name);

public sealed record UpdatePreferencesRequest(
    string ThemeId,
    bool QuietMode,
    bool EmailsEnabled,
    int ReserveTargetMonths,
    int MonthCloseDay,
    bool IncludeAbsoluteValuesInRetrospective);

public sealed record ProfileResponse(
    Guid UsuarioId,
    string Name,
    string Email,
    bool OnboardingCompleted,
    string ThemeId,
    bool QuietMode,
    bool EmailsEnabled,
    int ReserveTargetMonths,
    int MonthCloseDay,
    bool IncludeAbsoluteValuesInRetrospective);

public sealed record CategoryMemberBreakdownRow(
    Guid UsuarioId,
    string Name,
    decimal Amount);

public sealed record CategoryMemberBreakdownResponse(
    string CompetenceYm,
    string CategoryKey,
    string CategoryName,
    SharingScope Scope,
    decimal Total,
    IReadOnlyList<CategoryMemberBreakdownRow> Members);
