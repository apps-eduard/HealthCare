namespace HealthCare.Application.Organizations;

public interface IOrganizationLimitService
{
    Task EnsureClinicCapacityAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task EnsureStaffCapacityAsync(Guid organizationId, CancellationToken cancellationToken = default);

    Task<OrganizationLimitSnapshot> GetSnapshotAsync(
        Guid organizationId,
        Guid? clinicId = null,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationLimitSnapshot
{
    public required Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

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
}
