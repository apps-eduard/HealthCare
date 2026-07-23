namespace HealthCare.Application.Authorization;

public interface IAuthorizationAuditLogger
{
    void PermissionDenied(string permission, string operation, string reasonCode);

    void CrossTenantDenied(string operation, string reasonCode, Guid? organizationId = null, Guid? clinicId = null);

    void ExplicitPlatformBypassUsed(string operation, Guid? organizationId = null, Guid? clinicId = null);

    void RoleAssignmentDenied(string actorRole, string targetRole, string reasonCode);

    void InactiveMembershipRejected(string operation);

    void UnknownPermissionRequested(string permission);
}
