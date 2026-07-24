using HealthCare.Application.Authorization;
using HealthCare.Contracts.Security;

namespace HealthCare.Application.Security;

public interface IOrganizationSecurityService
{
    Task<OrganizationSecuritySessionListResponse> ListSessionsAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<RevokeOrganizationSessionsResponse> RevokeStaffSessionsAsync(
        Guid staffMemberId,
        RevokeOrganizationSessionsRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<CompromisedAccountResponseResult> RespondToCompromisedAccountAsync(
        Guid staffMemberId,
        CompromisedAccountResponseRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationFailedLoginSummaryResponse> GetFailedLoginSummaryAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationSecurityEventSummaryResponse> GetAuthorizationDenialSummaryAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationSecurityEventSummaryResponse> GetCrossClinicAttemptSummaryAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationSecurityException : Exception
{
    public OrganizationSecurityException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static OrganizationSecurityException AccessDenied() =>
        new(OrganizationSecurityErrorCodes.AccessDenied, "Organization security access is denied.", 403);

    public static OrganizationSecurityException InvalidScope() =>
        new(OrganizationSecurityErrorCodes.InvalidScope, "The requested security scope is invalid.", 400);

    public static OrganizationSecurityException OrganizationScopeRequired() =>
        new(OrganizationSecurityErrorCodes.OrganizationScopeRequired, "An organization scope is required.", 400);

    public static OrganizationSecurityException ClinicNotFound() =>
        new(OrganizationSecurityErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static OrganizationSecurityException TargetNotFound() =>
        new(OrganizationSecurityErrorCodes.TargetNotFound, "The target user or staff member was not found.", 404);

    public static OrganizationSecurityException InvalidDateRange() =>
        new(OrganizationSecurityErrorCodes.InvalidDateRange, "The security event date range is invalid.", 400);

    public static OrganizationSecurityException OrganizationNotFound() =>
        new(OrganizationSecurityErrorCodes.OrganizationNotFound, "Organization was not found.", 404);

    public static OrganizationSecurityException PlatformAdminProtected() =>
        new(OrganizationSecurityErrorCodes.PlatformAdminProtected, "Platform Admin accounts cannot be targeted.", 403);

    public static OrganizationSecurityException LastAdminProtected() =>
        new(OrganizationSecurityErrorCodes.LastAdminProtected, "The last Organization Admin cannot be deactivated.", 409);

    public static OrganizationSecurityException SelfCompromiseDenied() =>
        new(OrganizationSecurityErrorCodes.SelfCompromiseDenied, "You cannot run compromise response on your own account.", 403);

    public static OrganizationSecurityException AlreadyInactive() =>
        new(OrganizationSecurityErrorCodes.AlreadyInactive, "The staff account is already inactive.", 409);
}
