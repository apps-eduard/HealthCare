using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Authorization;

/// <summary>
/// Structured authorization audit events. Never logs tokens, passwords, or clinical payloads.
/// Persists organization-scoped operational events via <see cref="IOrganizationAuditRecorder"/>.
/// </summary>
public sealed class AuthorizationAuditLogger : IAuthorizationAuditLogger
{
    public const string CorrelationIdItemKey = "CorrelationId";

    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISecurityEventRecorder _securityEvents;
    private readonly IOrganizationAuditRecorder _organizationAudits;
    private readonly ILogger<AuthorizationAuditLogger> _logger;

    public AuthorizationAuditLogger(
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        ISecurityEventRecorder securityEvents,
        IOrganizationAuditRecorder organizationAudits,
        ILogger<AuthorizationAuditLogger> logger)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _securityEvents = securityEvents;
        _organizationAudits = organizationAudits;
        _logger = logger;
    }

    public void PermissionDenied(string permission, string operation, string reasonCode)
    {
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Authorization denied. Event=permission_denied UserId={UserId} Permission={Permission} Operation={Operation} OrganizationId={OrganizationId} ClinicId={ClinicId} CorrelationId={CorrelationId} ReasonCode={ReasonCode}",
            _currentUser.UserId,
            permission,
            operation,
            _currentUser.OrganizationId,
            _currentUser.ClinicId,
            correlationId,
            reasonCode);

        _securityEvents.TryRecord(new SecurityEventWrite
        {
            EventType = SecurityEventType.PermissionDenied,
            Operation = Truncate(operation, 128),
            ReasonCode = Truncate(reasonCode, 128),
            OrganizationId = _currentUser.OrganizationId,
            ClinicId = _currentUser.ClinicId,
            ActorUserId = _currentUser.UserId,
            CorrelationId = correlationId,
        });

        TryPersistOrgAudit(
            category: "security",
            action: "permission_denied",
            resultCode: reasonCode,
            organizationId: _currentUser.OrganizationId,
            clinicId: _currentUser.ClinicId,
            resourceType: "permission",
            resourceId: null,
            correlationId: correlationId);
    }

    public void CrossTenantDenied(string operation, string reasonCode, Guid? organizationId = null, Guid? clinicId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Authorization denied. Event=cross_tenant_denied UserId={UserId} Operation={Operation} OrganizationId={OrganizationId} ClinicId={ClinicId} CorrelationId={CorrelationId} ReasonCode={ReasonCode}",
            _currentUser.UserId,
            operation,
            orgId,
            clinic,
            correlationId,
            reasonCode);

        _securityEvents.TryRecord(new SecurityEventWrite
        {
            EventType = SecurityEventType.CrossTenantDenied,
            Operation = Truncate(operation, 128),
            ReasonCode = Truncate(reasonCode, 128),
            OrganizationId = orgId,
            ClinicId = clinic,
            ActorUserId = _currentUser.UserId,
            CorrelationId = correlationId,
        });

        TryPersistOrgAudit(
            category: "security",
            action: "cross_clinic_denied",
            resultCode: reasonCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "operation",
            resourceId: null,
            correlationId: correlationId);
    }

    public void ExplicitPlatformBypassUsed(string operation, Guid? organizationId = null, Guid? clinicId = null)
    {
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Authorization event. Event=platform_bypass_used UserId={UserId} Operation={Operation} OrganizationId={OrganizationId} ClinicId={ClinicId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            organizationId,
            clinicId,
            correlationId);

        TryPersistOrgAudit(
            category: "security",
            action: "platform_bypass_used",
            resultCode: "succeeded",
            organizationId: organizationId,
            clinicId: clinicId,
            resourceType: "operation",
            resourceId: null,
            correlationId: correlationId);
    }

    public void RoleAssignmentDenied(string actorRole, string targetRole, string reasonCode) =>
        _logger.LogInformation(
            "Authorization denied. Event=role_assignment_denied UserId={UserId} ActorRole={ActorRole} TargetRole={TargetRole} CorrelationId={CorrelationId} ReasonCode={ReasonCode}",
            _currentUser.UserId,
            actorRole,
            targetRole,
            CorrelationId(),
            reasonCode);

    public void InactiveMembershipRejected(string operation) =>
        _logger.LogInformation(
            "Authorization denied. Event=inactive_membership UserId={UserId} Operation={Operation} CorrelationId={CorrelationId} ReasonCode={ReasonCode}",
            _currentUser.UserId,
            operation,
            CorrelationId(),
            Contracts.Identity.AuthorizationErrorCodes.InactiveMembership);

    public void UnknownPermissionRequested(string permission) =>
        _logger.LogWarning(
            "Authorization denied. Event=unknown_permission UserId={UserId} Permission={Permission} CorrelationId={CorrelationId} ReasonCode={ReasonCode}",
            _currentUser.UserId,
            permission,
            CorrelationId(),
            Contracts.Identity.AuthorizationErrorCodes.InvalidPermission);

    public void ClinicOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Clinic operation. Event=clinic_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            correlationId);

        TryPersistOrgAudit(
            category: "clinic",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "clinic",
            resourceId: clinic,
            correlationId: correlationId);
    }

    public void StaffOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? staffMemberId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Staff operation. Event=staff_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} StaffMemberId={StaffMemberId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            staffMemberId,
            correlationId);

        TryPersistOrgAudit(
            category: "staff",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "staff",
            resourceId: staffMemberId,
            correlationId: correlationId);
    }

    public void PatientOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? patientId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Patient operation. Event=patient_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} PatientId={PatientId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            patientId,
            correlationId);

        TryPersistOrgAudit(
            category: "patient",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "patient",
            resourceId: patientId,
            correlationId: correlationId);
    }

    public void AppointmentOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? appointmentId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Appointment operation. Event=appointment_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} AppointmentId={AppointmentId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            appointmentId,
            correlationId);

        TryPersistOrgAudit(
            category: "appointment",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "appointment",
            resourceId: appointmentId,
            correlationId: correlationId);
    }

    public void AvailabilityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? doctorStaffMemberId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Availability operation. Event=availability_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} DoctorStaffMemberId={DoctorStaffMemberId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            doctorStaffMemberId,
            correlationId);

        TryPersistOrgAudit(
            category: "availability",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "doctor",
            resourceId: doctorStaffMemberId,
            correlationId: correlationId);
    }

    public void ReminderOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? reminderId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Reminder operation. Event=reminder_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} ReminderId={ReminderId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            reminderId,
            correlationId);

        TryPersistOrgAudit(
            category: "reminder",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "reminder",
            resourceId: reminderId,
            correlationId: correlationId);
    }

    public void SummaryOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? runId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Summary operation. Event=summary_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} RunId={RunId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            runId,
            correlationId);

        TryPersistOrgAudit(
            category: "summary",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "summary_run",
            resourceId: runId,
            correlationId: correlationId);
    }

    public void ReportOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        string? reportType = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Report operation. Event=report_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} ReportType={ReportType} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            reportType,
            correlationId);

        TryPersistOrgAudit(
            category: "report",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: reportType,
            resourceId: null,
            correlationId: correlationId);
    }

    public void SecurityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? targetUserId = null)
    {
        var orgId = organizationId ?? _currentUser.OrganizationId;
        var clinic = clinicId ?? _currentUser.ClinicId;
        var correlationId = CorrelationId();
        _logger.LogInformation(
            "Security operation. Event=security_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} TargetUserId={TargetUserId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            orgId,
            clinic,
            targetUserId,
            correlationId);

        TryPersistOrgAudit(
            category: "security",
            action: operation,
            resultCode: resultCode,
            organizationId: orgId,
            clinicId: clinic,
            resourceType: "user",
            resourceId: targetUserId,
            correlationId: correlationId);
    }

    private void TryPersistOrgAudit(
        string category,
        string action,
        string resultCode,
        Guid? organizationId,
        Guid? clinicId,
        string? resourceType,
        Guid? resourceId,
        string? correlationId)
    {
        if (organizationId is null || organizationId == Guid.Empty)
        {
            return;
        }

        _organizationAudits.TryRecord(new OrganizationAuditWrite
        {
            OrganizationId = organizationId.Value,
            ClinicId = clinicId,
            ActorUserId = _currentUser.UserId,
            Category = Truncate(category, 64),
            Action = Truncate(action, 128),
            ResultCode = Truncate(resultCode, 64),
            ResourceType = TruncateNullable(resourceType, 64),
            ResourceId = resourceId,
            CorrelationId = TruncateNullable(correlationId, 64),
        });
    }

    private string CorrelationId()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http?.Items.TryGetValue(CorrelationIdItemKey, out var value) == true
            && value is string s
            && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return http?.TraceIdentifier ?? string.Empty;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }

    private static string? TruncateNullable(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }
}
