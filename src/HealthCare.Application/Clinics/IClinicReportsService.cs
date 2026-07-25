using HealthCare.Application.Authorization;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public interface IClinicReportsService
{
    Task<ClinicAppointmentReportResponse> GetAppointmentsAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicDoctorAppointmentsReportResponse> GetDoctorsAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicPatientEnrollmentReportResponse> GetPatientsAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicOperationsReportResponse> GetRemindersAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicReportException : Exception
{
    public ClinicReportException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static ClinicReportException AccessDenied() =>
        new(ClinicReportErrorCodes.AccessDenied, "Clinic reports access is denied.", 403);

    public static ClinicReportException InvalidScope() =>
        new(ClinicReportErrorCodes.InvalidScope, "The requested clinic report scope is invalid.", 400);

    public static ClinicReportException ClinicScopeRequired() =>
        new(ClinicReportErrorCodes.ClinicScopeRequired, "A clinic scope is required.", 400);

    public static ClinicReportException ClinicNotFound() =>
        new(ClinicReportErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static ClinicReportException InvalidDateRange() =>
        new(ClinicReportErrorCodes.InvalidDateRange, "The report date range is invalid.", 400);
}
