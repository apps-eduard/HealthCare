namespace HealthCare.Application.Authorization;

/// <summary>
/// Reusable rules for future staff role-assignment APIs. No public endpoint yet.
/// </summary>
public interface IRoleAssignmentAuthorizationService
{
    /// <summary>
    /// Ensures the actor may assign <paramref name="targetRole"/> to <paramref name="targetUserId"/>
    /// within the given tenant scope.
    /// </summary>
    void EnsureCanAssignRole(RoleAssignmentRequest request);

    bool CanAssignRole(RoleAssignmentRequest request);
}

public sealed record RoleAssignmentRequest(
    Guid ActorUserId,
    string ActorRole,
    Guid? ActorOrganizationId,
    Guid? ActorClinicId,
    Guid TargetUserId,
    string TargetRole,
    Guid? TargetOrganizationId,
    Guid? TargetClinicId,
    bool TargetHasPatientRole = false,
    bool TargetHasStaffMembership = false);
