using HealthCare.Application.Authorization;

namespace HealthCare.UnitTests;

internal class NoOpAuthorizationAuditLogger : IAuthorizationAuditLogger
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

    public virtual void ClinicOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        IReadOnlyList<string>? changedFields = null)
    {
    }

    public virtual void StaffOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? staffMemberId = null,
        IReadOnlyList<string>? changedFields = null)
    {
    }

    public virtual void PatientOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? patientId = null)
    {
    }

    public virtual void AppointmentOperation(
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

    public virtual void ReminderOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? reminderId = null)
    {
    }

    public virtual void SummaryOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? runId = null)
    {
    }

    public void ReportOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        string? reportType = null)
    {
    }

    public void SecurityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? targetUserId = null)
    {
    }

    public virtual void OrganizationOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null)
    {
    }
}
