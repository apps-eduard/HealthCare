using HealthCare.Domain.Identity;

namespace HealthCare.Domain.Patients;

/// <summary>
/// Global patient profile. Clinic relationships are modeled separately via <see cref="ClinicPatient"/>.
/// Organization/clinic scope is never taken from the client for identity resolution.
/// </summary>
public sealed class Patient
{
    public Guid Id { get; set; }

    /// <summary>
    /// Linked platform account. Null until a PATIENT user is linked server-side.
    /// At most one user per patient; at most one patient per user.
    /// </summary>
    public Guid? UserId { get; set; }

    public required string FirstName { get; set; }

    public string? MiddleName { get; set; }

    public required string LastName { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? MobileNumber { get; set; }

    public string? PreferredLanguage { get; set; }

    public string? Address { get; set; }

    public string? EmergencyContact { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optimistic concurrency token. Incremented on each successful profile update.
    /// </summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ApplicationUser? User { get; set; }

    public ICollection<ClinicPatient> ClinicPatients { get; set; } = new List<ClinicPatient>();
}
