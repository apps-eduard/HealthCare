namespace HealthCare.Contracts.Doctors;

public static class DoctorDashboardErrorCodes
{
    public const string AccessDenied = "doctor_dashboard.access_denied";
    public const string InvalidScope = "doctor_dashboard.invalid_scope";
    public const string ClinicScopeRequired = "doctor_dashboard.clinic_scope_required";
    public const string DoctorScopeRequired = "doctor_dashboard.doctor_scope_required";
    public const string ClinicNotFound = "doctor_dashboard.clinic_not_found";
    public const string DoctorNotFound = "doctor_dashboard.doctor_not_found";
    public const string InvalidDate = "doctor_dashboard.invalid_date";
}

/// <summary>
/// Query for <c>GET /api/v1/doctor/dashboard</c>.
/// <see cref="ClinicId"/> and <see cref="DoctorStaffMemberId"/> are required for PLATFORM_ADMIN
/// with explicit bypass; ignored for DOCTOR (membership is authoritative).
/// </summary>
public sealed class DoctorDashboardQuery
{
    public Guid? ClinicId { get; init; }

    public Guid? DoctorStaffMemberId { get; init; }

    /// <summary>Optional clinic-local date (yyyy-MM-dd). When omitted, uses the clinic's local today.</summary>
    public string? Date { get; init; }
}

public sealed class DoctorDashboardResponse
{
    public required Guid DoctorStaffMemberId { get; init; }

    public required string DoctorDisplayName { get; init; }

    public required Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public required string DefaultTimeZoneId { get; init; }

    public required string LocalDashboardDate { get; init; }

    public required string TimeZoneStrategy { get; init; }

    public int TodayAppointmentCount { get; init; }

    public int UpcomingAppointmentCount { get; init; }

    public int CheckedInAppointmentCount { get; init; }

    public int AwaitingCompletionCount { get; init; }

    public int RecentNoShowCount { get; init; }

    public DoctorDashboardNextAppointment? NextAppointment { get; init; }

    public int AvailabilityWarningCount { get; init; }

    public required IReadOnlyList<string> AvailabilityWarnings { get; init; }
}

public sealed class DoctorDashboardNextAppointment
{
    public required Guid AppointmentId { get; init; }

    public required DateTimeOffset AppointmentDateUtc { get; init; }

    public required string Status { get; init; }

    public required string PatientDisplayName { get; init; }
}
