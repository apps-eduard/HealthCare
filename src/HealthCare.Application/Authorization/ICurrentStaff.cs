namespace HealthCare.Application.Authorization;

/// <summary>
/// Active clinic staff membership for the authenticated user, resolved from the database.
/// </summary>
public interface ICurrentStaff
{
    bool HasActiveMembership { get; }

    Guid StaffMemberId { get; }

    Guid OrganizationId { get; }

    Guid ClinicId { get; }

    string Role { get; }
}
