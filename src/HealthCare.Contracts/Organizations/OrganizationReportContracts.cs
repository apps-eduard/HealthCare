namespace HealthCare.Contracts.Organizations;

public static class OrganizationReportErrorCodes
{
    public const string AccessDenied = "organization_reports.access_denied";
    public const string InvalidScope = "organization_reports.invalid_scope";
    public const string OrganizationScopeRequired = "organization_reports.organization_scope_required";
    public const string ClinicNotFound = "organization_reports.clinic_not_found";
    public const string InvalidDateRange = "organization_reports.invalid_date_range";
    public const string OrganizationNotFound = "organization_reports.organization_not_found";
    public const string UnknownReport = "organization_reports.unknown_report";
}

public static class OrganizationReportTypes
{
    public const string Appointments = "appointments";
    public const string Staff = "staff";
    public const string Patients = "patients";
    public const string Availability = "availability";
    public const string ReminderFailures = "reminder-failures";
    public const string SummaryFailures = "summary-failures";

    public static readonly IReadOnlyList<string> All =
    [
        Appointments,
        Staff,
        Patients,
        Availability,
        ReminderFailures,
        SummaryFailures,
    ];
}

/// <summary>
/// Common filters for organization report APIs.
/// OrganizationId is honored only for PLATFORM_ADMIN with explicit bypass.
/// </summary>
public sealed class OrganizationReportQuery
{
    public Guid? OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    /// <summary>Inclusive clinic-local start date (yyyy-MM-dd). Defaults to today when omitted with ToDate.</summary>
    public string? FromDate { get; init; }

    /// <summary>Inclusive clinic-local end date (yyyy-MM-dd). Defaults to today when omitted with FromDate.</summary>
    public string? ToDate { get; init; }
}

public sealed class OrganizationReportContext
{
    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public Guid? SelectedClinicId { get; init; }

    public string? SelectedClinicName { get; init; }

    public string? FromDate { get; init; }

    public string? ToDate { get; init; }

    public string? TimeZoneId { get; init; }

    /// <summary><c>clinic</c> or <c>per_clinic_local</c>.</summary>
    public required string TimeZoneStrategy { get; init; }
}

public sealed class OrganizationAppointmentReportResponse
{
    public required OrganizationReportContext Context { get; init; }

    public required OrganizationAppointmentReportTotals Totals { get; init; }

    public IReadOnlyList<OrganizationReportClinicAppointmentCount> ByClinic { get; init; } = [];

    public IReadOnlyList<OrganizationReportStatusCount> ByStatus { get; init; } = [];

    public IReadOnlyList<OrganizationReportDoctorAppointmentCount> ByDoctor { get; init; } = [];
}

public sealed class OrganizationAppointmentReportTotals
{
    public int TotalAppointments { get; init; }

    public int CancellationCount { get; init; }

    public int NoShowCount { get; init; }

    public int CompletedCount { get; init; }

    public int ConfirmedCount { get; init; }

    public int RequestedCount { get; init; }
}

public sealed class OrganizationReportClinicAppointmentCount
{
    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public int TotalAppointments { get; init; }

    public int CancellationCount { get; init; }

    public int NoShowCount { get; init; }
}

public sealed class OrganizationReportStatusCount
{
    public required string Status { get; init; }

    public int Count { get; init; }
}

public sealed class OrganizationReportDoctorAppointmentCount
{
    public Guid DoctorStaffMemberId { get; init; }

    public required string DoctorDisplayName { get; init; }

    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public int Count { get; init; }
}

public sealed class OrganizationStaffReportResponse
{
    public required OrganizationReportContext Context { get; init; }

    public int TotalActiveStaff { get; init; }

    public int TotalInactiveStaff { get; init; }

    public IReadOnlyList<OrganizationReportStaffClinicCount> ByClinic { get; init; } = [];
}

public sealed class OrganizationReportStaffClinicCount
{
    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public int ActiveStaffCount { get; init; }

    public int InactiveStaffCount { get; init; }

    public int DoctorCount { get; init; }

    public int NurseCount { get; init; }

    public int ReceptionistCount { get; init; }

    public int ClinicAdminCount { get; init; }

    public int OrganizationAdminCount { get; init; }
}

public sealed class OrganizationPatientReportResponse
{
    public required OrganizationReportContext Context { get; init; }

    public int TotalActiveEnrollments { get; init; }

    public int TotalInactiveEnrollments { get; init; }

    public int DistinctPatientCount { get; init; }

    public IReadOnlyList<OrganizationReportPatientClinicCount> ByClinic { get; init; } = [];
}

public sealed class OrganizationReportPatientClinicCount
{
    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public int ActiveEnrollmentCount { get; init; }

    public int InactiveEnrollmentCount { get; init; }
}

public sealed class OrganizationAvailabilityReportResponse
{
    public required OrganizationReportContext Context { get; init; }

    public IReadOnlyList<OrganizationReportAvailabilityClinicCoverage> ByClinic { get; init; } = [];
}

public sealed class OrganizationReportAvailabilityClinicCoverage
{
    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public bool ClinicIsActive { get; init; }

    public int ActiveDoctorCount { get; init; }

    public int DoctorsWithActiveAvailability { get; init; }

    public int ActiveAvailabilityWindowCount { get; init; }

    public int AvailabilityExceptionCount { get; init; }

    public bool HasCoverageGap { get; init; }
}

public sealed class OrganizationReminderFailureReportResponse
{
    public required OrganizationReportContext Context { get; init; }

    public int FailedCount { get; init; }

    public IReadOnlyList<OrganizationReportClinicFailureCount> ByClinic { get; init; } = [];

    public IReadOnlyList<OrganizationReportReminderFailureItem> Items { get; init; } = [];
}

public sealed class OrganizationSummaryFailureReportResponse
{
    public required OrganizationReportContext Context { get; init; }

    public int FailedCount { get; init; }

    public IReadOnlyList<OrganizationReportClinicFailureCount> ByClinic { get; init; } = [];

    public IReadOnlyList<OrganizationReportSummaryFailureItem> Items { get; init; } = [];
}

public sealed class OrganizationReportClinicFailureCount
{
    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public int FailedCount { get; init; }
}

public sealed class OrganizationReportReminderFailureItem
{
    public Guid ReminderId { get; init; }

    public Guid AppointmentId { get; init; }

    public Guid ClinicId { get; init; }

    public required string ReminderType { get; init; }

    public DateTimeOffset ScheduledAtUtc { get; init; }

    public int AttemptCount { get; init; }

    public string? ErrorCode { get; init; }

    public string? BackgroundJobId { get; init; }
}

public sealed class OrganizationReportSummaryFailureItem
{
    public Guid RunId { get; init; }

    public Guid ClinicId { get; init; }

    public required string SummaryDate { get; init; }

    public DateTimeOffset ScheduledAtUtc { get; init; }

    public int AttemptCount { get; init; }

    public string? LastErrorCode { get; init; }

    public string? BackgroundJobId { get; init; }
}
