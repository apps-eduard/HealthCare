namespace HealthCare.Contracts.Identity;

public sealed class CurrentUserResponse
{
    public required Guid UserId { get; init; }

    public string? Email { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    public Guid? OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    public Guid? PatientId { get; init; }

    public Guid? StaffMemberId { get; init; }

    public bool HasActiveStaffMembership { get; init; }

    public bool HasLinkedPatient { get; init; }
}
