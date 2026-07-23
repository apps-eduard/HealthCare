using HealthCare.Domain.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;

namespace HealthCare.Domain.Staff;

/// <summary>
/// Clinic staff membership. MVP: one clinic per staff member.
/// OrganizationId and ClinicId are server-owned scope fields; never trust client-supplied values.
/// </summary>
public sealed class StaffMember
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClinicId { get; set; }

    /// <summary>
    /// Staff role name. Must match a value from <see cref="AppRoles"/>.
    /// </summary>
    public required string Role { get; set; }

    public string? JobTitle { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ApplicationUser? User { get; set; }

    public Organization? Organization { get; set; }

    public Clinic? Clinic { get; set; }
}
