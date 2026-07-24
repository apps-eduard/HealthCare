namespace HealthCare.Contracts.Clinics;

public static class ClinicDashboardErrorCodes
{
    public const string AccessDenied = "clinic_dashboard.access_denied";
    public const string InvalidScope = "clinic_dashboard.invalid_scope";
    public const string ClinicScopeRequired = "clinic_dashboard.clinic_scope_required";
    public const string ClinicNotFound = "clinic_dashboard.clinic_not_found";
    public const string InvalidDate = "clinic_dashboard.invalid_date";
}

/// <summary>
/// Query for <c>GET /api/v1/clinic/dashboard</c>.
/// <see cref="ClinicId"/> is required for PLATFORM_ADMIN with explicit bypass; ignored for CLINIC_ADMIN
/// (membership clinic is authoritative).
/// </summary>
public sealed class ClinicDashboardQuery
{
    public Guid? ClinicId { get; init; }

    /// <summary>Optional clinic-local date (yyyy-MM-dd). When omitted, uses the clinic's local today.</summary>
    public string? Date { get; init; }
}

public sealed class ClinicDashboardResponse
{
    public required Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public required string DefaultTimeZoneId { get; init; }

    public required string DashboardDate { get; init; }

    public required string TimeZoneStrategy { get; init; }

    public int ActiveStaffCount { get; init; }

    public int ActiveDoctorCount { get; init; }

    public int ActivePatientCount { get; init; }

    public int TodayAppointmentCount { get; init; }

    public required ClinicDashboardAppointmentByStatus TodayAppointmentsByStatus { get; init; }

    public int MonthlyAppointmentCount { get; init; }

    public int FailedReminderCount { get; init; }

    public required ClinicDashboardOperationalWarnings OperationalWarnings { get; init; }
}

public sealed class ClinicDashboardAppointmentByStatus
{
    public int RequestedCount { get; init; }

    public int ConfirmedCount { get; init; }

    public int CheckedInCount { get; init; }

    public int InProgressCount { get; init; }

    public int CompletedCount { get; init; }

    public int CancelledCount { get; init; }

    public int NoShowCount { get; init; }
}

public sealed class ClinicDashboardOperationalWarnings
{
    public int FailedReminderCount { get; init; }

    public int FailedClinicSummaryCount { get; init; }

    public bool MissingActiveDoctor { get; init; }

    public bool MissingAvailability { get; init; }
}
