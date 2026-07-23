namespace HealthCare.Contracts.Patients;

public sealed class PatientProfileResponse
{
    public Guid Id { get; init; }

    public string FirstName { get; init; } = string.Empty;

    public string? MiddleName { get; init; }

    public string LastName { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    public string? Gender { get; init; }

    public string? MobileNumber { get; init; }

    public string? PreferredLanguage { get; init; }

    public string? Address { get; init; }

    public string? EmergencyContact { get; init; }

    public bool IsActive { get; init; }

    public Guid? LinkedUserId { get; init; }

    /// <summary>
    /// Optimistic concurrency version for subsequent PATCH requests.
    /// </summary>
    public int Version { get; init; }
}
