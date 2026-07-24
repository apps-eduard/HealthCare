using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public sealed class ClinicManagementException : Exception
{
    public ClinicManagementException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static ClinicManagementException NotFound() =>
        new(ClinicManagementErrorCodes.NotFound, "Clinic was not found.", 404);

    public static ClinicManagementException AccessDenied() =>
        new(ClinicManagementErrorCodes.AccessDenied, "Clinic management access is denied.", 403);

    public static ClinicManagementException InvalidScope() =>
        new(ClinicManagementErrorCodes.InvalidScope, "The requested clinic scope is invalid.", 400);

    public static ClinicManagementException OrganizationScopeRequired() =>
        new(ClinicManagementErrorCodes.OrganizationScopeRequired, "An organization scope is required.", 400);

    public static ClinicManagementException SlugInUse() =>
        new(ClinicManagementErrorCodes.SlugInUse, "Clinic slug is already in use.", 409);

    public static ClinicManagementException InvalidTimezone() =>
        new(ClinicManagementErrorCodes.InvalidTimezone, "Clinic timezone is invalid.", 400);

    public static ClinicManagementException InactiveOrganization() =>
        new(ClinicManagementErrorCodes.InactiveOrganization, "The organization is inactive.", 409);

    public static ClinicManagementException ConcurrencyConflict() =>
        new(ClinicManagementErrorCodes.ConcurrencyConflict, "Clinic was modified by another request.", 409);

    public static ClinicManagementException DeactivationNotAllowed(string title) =>
        new(ClinicManagementErrorCodes.DeactivationNotAllowed, title, 409);

    public static ClinicManagementException ActivationNotAllowed(string title) =>
        new(ClinicManagementErrorCodes.ActivationNotAllowed, title, 409);

    public static ClinicManagementException EmptyUpdate() =>
        new(ClinicManagementErrorCodes.EmptyUpdate, "No clinic fields were provided to update.", 400);

    public static ClinicManagementException InitialAdminFailed(string detail) =>
        new(ClinicManagementErrorCodes.InitialAdminFailed, detail, 400);

    public static ClinicManagementException SlugInvalid() =>
        new(ClinicManagementErrorCodes.SlugInvalid, "Clinic slug format is invalid.", 400);
}
