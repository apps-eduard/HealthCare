using HealthCare.Contracts.Common;

namespace HealthCare.Contracts.Patients;

/// <summary>
/// Authenticated Patient clinic directory search (PM-4).
/// Staff directory DTOs must not be reused for this surface.
/// </summary>
public sealed class PatientClinicSearchRequest
{
    /// <summary>
    /// Optional case-insensitive match against clinic name, city, or address text.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Optional filter against the existing clinic specialty string (not a specialty catalog).
    /// </summary>
    public string? Specialty { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

/// <summary>Patient-safe clinic list row. Navigation uses public <see cref="ClinicCode"/>.</summary>
public sealed class PatientClinicListItemResponse
{
    public string ClinicCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? City { get; init; }

    public string? Specialty { get; init; }

    public string? TimeZoneId { get; init; }

    /// <summary>True when the current Patient already has a ClinicPatient enrollment.</summary>
    public bool IsEnrolled { get; init; }
}

/// <summary>Patient-safe clinic details for discovery and enrollment.</summary>
public sealed class PatientClinicDetailResponse
{
    public string ClinicCode { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Specialty { get; init; }

    public string? Description { get; init; }

    public string? City { get; init; }

    public string? Address { get; init; }

    public string? PhoneNumber { get; init; }

    public string? Email { get; init; }

    public string? TimeZoneId { get; init; }

    public bool IsEnrolled { get; init; }

    public string? EnrollmentStatus { get; init; }
}
