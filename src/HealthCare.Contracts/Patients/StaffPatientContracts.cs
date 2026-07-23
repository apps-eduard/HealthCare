namespace HealthCare.Contracts.Patients;

public sealed class StaffPatientSearchRequest
{
    public string? Search { get; init; }

    public string? LocalPatientNumber { get; init; }

    public bool? PatientIsActive { get; init; }

    /// <summary>
    /// Allowed values: Active, Inactive.
    /// </summary>
    public string? ClinicPatientStatus { get; init; }

    /// <summary>
    /// Optional clinic filter for ORGANIZATION_ADMIN (must belong to their organization)
    /// or PLATFORM_ADMIN with explicit bypass.
    /// Ignored for clinic-scoped staff (their ClinicId is always used).
    /// </summary>
    public Guid? ClinicId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string SortBy { get; init; } = "registeredAtUtc";

    public string SortDirection { get; init; } = "desc";
}

public class StaffPatientSummaryResponse
{
    public Guid PatientId { get; init; }

    public Guid ClinicPatientId { get; init; }

    public Guid ClinicId { get; init; }

    public string LocalPatientNumber { get; init; } = string.Empty;

    public string FirstName { get; init; } = string.Empty;

    public string? MiddleName { get; init; }

    public string LastName { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    public string? Gender { get; init; }

    public string? MobileNumber { get; init; }

    public string? PreferredLanguage { get; init; }

    public bool PatientIsActive { get; init; }

    public string ClinicPatientStatus { get; init; } = string.Empty;

    public DateTimeOffset RegisteredAtUtc { get; init; }

    public int Version { get; init; }
}

public sealed class StaffPatientDetailResponse : StaffPatientSummaryResponse
{
    public string? Address { get; init; }

    public string? EmergencyContact { get; init; }
}

/// <summary>
/// Clinic-level administration only. Does not update global Patient demographics.
/// </summary>
public sealed class UpdateClinicPatientRequest
{
    public int ExpectedVersion { get; init; }

    /// <summary>
    /// Allowed values: Active, Inactive.
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
