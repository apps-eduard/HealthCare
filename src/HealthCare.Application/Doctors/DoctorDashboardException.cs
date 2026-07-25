using HealthCare.Contracts.Doctors;

namespace HealthCare.Application.Doctors;

public sealed class DoctorDashboardException : Exception
{
    public DoctorDashboardException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static DoctorDashboardException AccessDenied() =>
        new(DoctorDashboardErrorCodes.AccessDenied, "Doctor dashboard access is denied.", 403);

    public static DoctorDashboardException InvalidScope() =>
        new(DoctorDashboardErrorCodes.InvalidScope, "The requested doctor dashboard scope is invalid.", 400);

    public static DoctorDashboardException ClinicScopeRequired() =>
        new(
            DoctorDashboardErrorCodes.ClinicScopeRequired,
            "An explicit clinic scope is required.",
            400);

    public static DoctorDashboardException DoctorScopeRequired() =>
        new(
            DoctorDashboardErrorCodes.DoctorScopeRequired,
            "An explicit doctor staff member scope is required.",
            400);

    public static DoctorDashboardException ClinicNotFound() =>
        new(DoctorDashboardErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static DoctorDashboardException DoctorNotFound() =>
        new(DoctorDashboardErrorCodes.DoctorNotFound, "Doctor was not found.", 404);

    public static DoctorDashboardException InvalidDate() =>
        new(DoctorDashboardErrorCodes.InvalidDate, "Dashboard date is invalid.", 400);
}
