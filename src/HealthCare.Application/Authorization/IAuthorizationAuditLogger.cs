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
    /// Clinic management operation audit. Must never include passwords, tokens, or PHI.
    /// </summary>
    void ClinicOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null);

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

    /// <summary>
    /// Appointment operational audit. Must never include passwords, tokens, or clinical note content.
    /// </summary>
    void AppointmentOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? appointmentId = null);

    /// <summary>
    /// Doctor availability operational audit. Must never include passwords, tokens, or clinical note content.
    /// </summary>
    void AvailabilityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? doctorStaffMemberId = null);

    /// <summary>
    /// Appointment reminder operational audit. Must never include payloads, secrets, or PHI.
    /// </summary>
    void ReminderOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? reminderId = null);

    /// <summary>
    /// Clinic appointment summary operational audit. Must never include payloads, secrets, or PHI.
    /// </summary>
    void SummaryOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? runId = null);

    /// <summary>
    /// Organization report operational audit. Must never include clinical payloads, secrets, or PHI.
    /// </summary>
    void ReportOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        string? reportType = null);

    /// <summary>
    /// Organization security operational audit. Must never include tokens, passwords, or PHI.
    /// </summary>
    void SecurityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? targetUserId = null);
}
