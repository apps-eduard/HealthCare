using HealthCare.Contracts.Identity;

namespace HealthCare.Application.Authorization;

public sealed class AuthorizationException : Exception
{
    public AuthorizationException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static AuthorizationException NotAuthenticated() =>
        new(AuthorizationErrorCodes.NotAuthenticated, "Authentication is required.", 401);

    public static AuthorizationException InvalidCurrentUser() =>
        new(AuthorizationErrorCodes.InvalidCurrentUser, "The current user context is invalid.");

    public static AuthorizationException AccountDisabled() =>
        new(AuthorizationErrorCodes.AccountDisabled, "This account is disabled.");

    public static AuthorizationException MissingStaffMembership() =>
        new(AuthorizationErrorCodes.MissingStaffMembership, "An active staff membership is required.");

    public static AuthorizationException InactiveStaffMembership() =>
        new(AuthorizationErrorCodes.InactiveStaffMembership, "The staff membership is inactive.");

    public static AuthorizationException OrganizationAccessDenied() =>
        new(AuthorizationErrorCodes.OrganizationAccessDenied, "Access to the requested organization is denied.");

    public static AuthorizationException ClinicAccessDenied() =>
        new(AuthorizationErrorCodes.ClinicAccessDenied, "Access to the requested clinic is denied.");

    public static AuthorizationException MissingPatientLinkage() =>
        new(AuthorizationErrorCodes.MissingPatientLinkage, "No patient profile is linked to this account.");

    public static AuthorizationException PatientSelfScopeDenied() =>
        new(AuthorizationErrorCodes.PatientSelfScopeDenied, "Access to the requested patient record is denied.");

    public static AuthorizationException Forbidden() =>
        new(AuthorizationErrorCodes.Forbidden, "Access is denied.");
}
