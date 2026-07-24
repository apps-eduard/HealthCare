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

    public void StaffOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? staffMemberId = null)
    {
    }

    public void PatientOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? patientId = null)
    {
    }

    public void AppointmentOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? appointmentId = null)
    {
    }

    public void AvailabilityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? doctorStaffMemberId = null)
    {
    }
}
