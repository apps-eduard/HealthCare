using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public sealed class OrganizationDashboardException : Exception
{
    public OrganizationDashboardException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static OrganizationDashboardException AccessDenied() =>
        new(OrganizationDashboardErrorCodes.AccessDenied, "Organization dashboard access is denied.", 403);

    public static OrganizationDashboardException InvalidScope() =>
        new(OrganizationDashboardErrorCodes.InvalidScope, "The requested organization dashboard scope is invalid.", 400);

    public static OrganizationDashboardException OrganizationScopeRequired() =>
        new(
            OrganizationDashboardErrorCodes.OrganizationScopeRequired,
            "An organization scope is required.",
            400);

    public static OrganizationDashboardException ClinicNotFound() =>
        new(OrganizationDashboardErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static OrganizationDashboardException InvalidDate() =>
        new(OrganizationDashboardErrorCodes.InvalidDate, "Dashboard date is invalid.", 400);

    public static OrganizationDashboardException OrganizationNotFound() =>
        new(OrganizationDashboardErrorCodes.OrganizationNotFound, "Organization was not found.", 404);
}
