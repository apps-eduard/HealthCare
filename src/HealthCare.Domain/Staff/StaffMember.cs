using HealthCare.Domain.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;

namespace HealthCare.Domain.Staff;

/// <summary>
/// Clinic staff membership. MVP: exactly one staff membership per user (unique UserId).
/// OrganizationId and ClinicId are server-owned scope fields; never trust client-supplied values.
/// </summary>
public sealed class StaffMember
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClinicId { get; set; }

    /// <summary>
    /// Staff role name. Must match a value from <see cref="AppRoles"/> (excluding PATIENT).
    /// </summary>
    public required string Role { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? JobTitle { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Optimistic concurrency token for staff profile/membership mutations.</summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ApplicationUser? User { get; set; }

    public Organization? Organization { get; set; }

    public Clinic? Clinic { get; set; }
}
