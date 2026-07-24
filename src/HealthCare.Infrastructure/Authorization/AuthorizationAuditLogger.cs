using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Authorization;

/// <summary>
/// Structured authorization audit events. Never logs tokens, passwords, or clinical payloads.
/// </summary>
public sealed class AuthorizationAuditLogger : IAuthorizationAuditLogger
{
    public const string CorrelationIdItemKey = "CorrelationId";

    private readonly ICurrentUser _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISecurityEventRecorder _securityEvents;
    private readonly ILogger<AuthorizationAuditLogger> _logger;

    public AuthorizationAuditLogger(
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        ISecurityEventRecorder securityEvents,
        ILogger<AuthorizationAuditLogger> logger)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _securityEvents = securityEvents;
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
    }

    public void ExplicitPlatformBypassUsed(string operation, Guid? organizationId = null, Guid? clinicId = null) =>
        _logger.LogInformation(
            "Authorization event. Event=platform_bypass_used UserId={UserId} Operation={Operation} OrganizationId={OrganizationId} ClinicId={ClinicId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            organizationId,
            clinicId,
            CorrelationId());

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

    public void StaffOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? staffMemberId = null) =>
        _logger.LogInformation(
            "Staff operation. Event=staff_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} StaffMemberId={StaffMemberId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            staffMemberId,
            CorrelationId());

    public void PatientOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? patientId = null) =>
        _logger.LogInformation(
            "Patient operation. Event=patient_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} PatientId={PatientId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            patientId,
            CorrelationId());

    public void AppointmentOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? appointmentId = null) =>
        _logger.LogInformation(
            "Appointment operation. Event=appointment_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} AppointmentId={AppointmentId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            appointmentId,
            CorrelationId());

    public void AvailabilityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? doctorStaffMemberId = null) =>
        _logger.LogInformation(
            "Availability operation. Event=availability_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} DoctorStaffMemberId={DoctorStaffMemberId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            doctorStaffMemberId,
            CorrelationId());

    public void ReminderOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? reminderId = null) =>
        _logger.LogInformation(
            "Reminder operation. Event=reminder_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} ReminderId={ReminderId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            reminderId,
            CorrelationId());

    public void SummaryOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? runId = null) =>
        _logger.LogInformation(
            "Summary operation. Event=summary_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} RunId={RunId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            runId,
            CorrelationId());

    public void ReportOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        string? reportType = null) =>
        _logger.LogInformation(
            "Report operation. Event=report_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} ReportType={ReportType} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            reportType,
            CorrelationId());

    public void SecurityOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? targetUserId = null) =>
        _logger.LogInformation(
            "Security operation. Event=security_operation UserId={UserId} Operation={Operation} ResultCode={ResultCode} OrganizationId={OrganizationId} ClinicId={ClinicId} TargetUserId={TargetUserId} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            operation,
            resultCode,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            targetUserId,
            CorrelationId());

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
}
