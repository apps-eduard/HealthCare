using HealthCare.Application.Authorization;

namespace HealthCare.UnitTests;

internal sealed class NoOpAuthorizationAuditLogger : IAuthorizationAuditLogger
{
    public void PermissionDenied(string permission, string operation, string reasonCode)
    {
    }

    public void CrossTenantDenied(string operation, string reasonCode, Guid? organizationId = null, Guid? clinicId = null)
    {
    }

    public void ExplicitPlatformBypassUsed(string operation, Guid? organizationId = null, Guid? clinicId = null)
    {
    }

    public void RoleAssignmentDenied(string actorRole, string targetRole, string reasonCode)
    {
    }

    public void InactiveMembershipRejected(string operation)
    {
    }

    public void UnknownPermissionRequested(string permission)
    {
    }
}
