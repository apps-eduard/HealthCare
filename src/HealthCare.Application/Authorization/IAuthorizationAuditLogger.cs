namespace HealthCare.Application.Authorization;

public interface IAuthorizationAuditLogger
{
    void PermissionDenied(string permission, string operation, string reasonCode);

    void CrossTenantDenied(string operation, string reasonCode, Guid? organizationId = null, Guid? clinicId = null);

    void ExplicitPlatformBypassUsed(string operation, Guid? organizationId = null, Guid? clinicId = null);

    void RoleAssignmentDenied(string actorRole, string targetRole, string reasonCode);

    void InactiveMembershipRejected(string operation);

    void UnknownPermissionRequested(string permission);

    /// <summary>
    /// Staff security/admin operation audit. Must never include passwords, tokens, or PHI.
    /// </summary>
    void StaffOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? staffMemberId = null);

    /// <summary>
    /// Patient directory / enrollment operation audit. Must never include passwords, tokens, or clinical PHI.
    /// </summary>
    void PatientOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? patientId = null);
}
