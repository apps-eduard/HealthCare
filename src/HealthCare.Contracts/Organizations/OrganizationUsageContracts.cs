namespace HealthCare.Contracts.Organizations;

public static class OrganizationUsageErrorCodes
{
    public const string AccessDenied = "organization_usage.access_denied";
    public const string InvalidScope = "organization_usage.invalid_scope";
    public const string OrganizationScopeRequired = "organization_usage.organization_scope_required";
    public const string ClinicNotFound = "organization_usage.clinic_not_found";
    public const string OrganizationNotFound = "organization_usage.organization_not_found";
}

public sealed class OrganizationUsageQuery
{
    public Guid? OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }
}

public sealed class OrganizationUsageResponse
{
    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public Guid? ClinicId { get; init; }

    public int ClinicCount { get; init; }

    public int ActiveClinicCount { get; init; }

    public int StaffCount { get; init; }

    public int ActiveStaffCount { get; init; }

    public int ActiveDoctorCount { get; init; }

    public int PatientCount { get; init; }

    public int MonthlyAppointmentCount { get; init; }

    public int MaxClinics { get; init; }

    public int MaxStaff { get; init; }

    public int RemainingClinicCapacity { get; init; }

    public int RemainingStaffCapacity { get; init; }

    public bool ClinicLimitWarning { get; init; }

    public bool StaffLimitWarning { get; init; }

    public bool ClinicLimitReached { get; init; }

    public bool StaffLimitReached { get; init; }

    public int WarningThresholdPercent { get; init; }

    public int AuditRetentionDays { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; }
}
