namespace HealthCare.Contracts.Organizations;

public static class OrganizationDashboardErrorCodes
{
    public const string AccessDenied = "organization_dashboard.access_denied";
    public const string InvalidScope = "organization_dashboard.invalid_scope";
    public const string OrganizationScopeRequired = "organization_dashboard.organization_scope_required";
    public const string ClinicNotFound = "organization_dashboard.clinic_not_found";
    public const string InvalidDate = "organization_dashboard.invalid_date";
    public const string OrganizationNotFound = "organization_dashboard.organization_not_found";
}

/// <summary>
/// Optional filters for <c>GET /api/v1/organization/dashboard</c>.
/// OrganizationId is honored only for PLATFORM_ADMIN with explicit bypass; ignored for ORGANIZATION_ADMIN.
/// </summary>
public sealed class OrganizationDashboardQuery
{
    public Guid? OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    /// <summary>Optional clinic-local date (yyyy-MM-DD). When omitted, each clinic uses its own local "today".</summary>
    public string? Date { get; init; }
}

public sealed class OrganizationDashboardResponse
{
    public required OrganizationDashboardOrganizationSummary Organization { get; init; }

    public required OrganizationDashboardStaffSummary Staff { get; init; }

    public required OrganizationDashboardPatientSummary Patients { get; init; }

    public required OrganizationDashboardAppointmentSummary Appointments { get; init; }

    public required OrganizationDashboardAlerts Alerts { get; init; }

    public required OrganizationDashboardContext Context { get; init; }
}

public sealed class OrganizationDashboardOrganizationSummary
{
    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public int ActiveClinicCount { get; init; }

    public int InactiveClinicCount { get; init; }

    public int TotalClinicCount { get; init; }
}

public sealed class OrganizationDashboardStaffSummary
{
    public int ActiveStaffCount { get; init; }

    public int InactiveStaffCount { get; init; }

    public int DoctorCount { get; init; }

    public int NurseCount { get; init; }

    public int ReceptionistCount { get; init; }

    public int ClinicAdminCount { get; init; }
}

public sealed class OrganizationDashboardPatientSummary
{
    public int TotalPatientCount { get; init; }

    public int ActivePatientCount { get; init; }

    public int InactivePatientCount { get; init; }
}

public sealed class OrganizationDashboardAppointmentSummary
{
    public int TotalAppointments { get; init; }

    public int RequestedCount { get; init; }

    public int ConfirmedCount { get; init; }

    public int CheckedInCount { get; init; }

    public int InProgressCount { get; init; }

    public int CompletedCount { get; init; }

    public int CancelledCount { get; init; }

    public int NoShowCount { get; init; }
}

public sealed class OrganizationDashboardAlerts
{
    public int FailedReminderCount { get; init; }

    public int FailedClinicSummaryCount { get; init; }

    public int ClinicsWithoutActiveDoctorCount { get; init; }

    public int ClinicsWithoutAvailabilityCount { get; init; }
}

public sealed class OrganizationDashboardContext
{
    public Guid? SelectedClinicId { get; init; }

    public string? SelectedClinicName { get; init; }

    /// <summary>
    /// Clinic IANA timezone when a single clinic is selected; null when aggregating across clinics
    /// (appointment "today" is computed per clinic local timezone — Option A).
    /// </summary>
    public string? TimeZoneId { get; init; }

    /// <summary>
    /// Selected operational date (yyyy-MM-DD). When aggregating all clinics without an explicit date,
    /// this may be null because each clinic uses its own local today.
    /// </summary>
    public string? DashboardDate { get; init; }

    /// <summary>
    /// Describes timezone strategy: <c>clinic</c> (single clinic) or <c>per_clinic_local</c> (Option A).
    /// </summary>
    public required string TimeZoneStrategy { get; init; }
}
