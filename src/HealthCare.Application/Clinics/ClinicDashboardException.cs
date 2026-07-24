using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public sealed class ClinicDashboardException : Exception
{
    public ClinicDashboardException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static ClinicDashboardException AccessDenied() =>
        new(ClinicDashboardErrorCodes.AccessDenied, "Clinic dashboard access is denied.", 403);

    public static ClinicDashboardException InvalidScope() =>
        new(ClinicDashboardErrorCodes.InvalidScope, "The requested clinic dashboard scope is invalid.", 400);

    public static ClinicDashboardException ClinicScopeRequired() =>
        new(
            ClinicDashboardErrorCodes.ClinicScopeRequired,
            "An explicit clinic scope is required.",
            400);

    public static ClinicDashboardException ClinicNotFound() =>
        new(ClinicDashboardErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static ClinicDashboardException InvalidDate() =>
        new(ClinicDashboardErrorCodes.InvalidDate, "Dashboard date is invalid.", 400);
}
