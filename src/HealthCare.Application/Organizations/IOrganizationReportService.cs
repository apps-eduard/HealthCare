using HealthCare.Application.Authorization;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public interface IOrganizationReportService
{
    Task<OrganizationAppointmentReportResponse> GetAppointmentsAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationStaffReportResponse> GetStaffAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationPatientReportResponse> GetPatientsAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationAvailabilityReportResponse> GetAvailabilityAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationReminderFailureReportResponse> GetReminderFailuresAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationSummaryFailureReportResponse> GetSummaryFailuresAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationReportCsvResult> ExportCsvAsync(
        string reportType,
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationReportCsvResult
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}

public sealed class OrganizationReportException : Exception
{
    public OrganizationReportException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static OrganizationReportException AccessDenied() =>
        new(OrganizationReportErrorCodes.AccessDenied, "Organization reports access is denied.", 403);

    public static OrganizationReportException InvalidScope() =>
        new(OrganizationReportErrorCodes.InvalidScope, "The requested organization report scope is invalid.", 400);

    public static OrganizationReportException OrganizationScopeRequired() =>
        new(
            OrganizationReportErrorCodes.OrganizationScopeRequired,
            "An organization scope is required.",
            400);

    public static OrganizationReportException ClinicNotFound() =>
        new(OrganizationReportErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static OrganizationReportException InvalidDateRange() =>
        new(OrganizationReportErrorCodes.InvalidDateRange, "The report date range is invalid.", 400);

    public static OrganizationReportException OrganizationNotFound() =>
        new(OrganizationReportErrorCodes.OrganizationNotFound, "Organization was not found.", 404);

    public static OrganizationReportException UnknownReport() =>
        new(OrganizationReportErrorCodes.UnknownReport, "The report type is not supported.", 400);
}
