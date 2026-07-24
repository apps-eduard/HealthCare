using HealthCare.Application.Authorization;
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
    private readonly ILogger<AuthorizationAuditLogger> _logger;

    public AuthorizationAuditLogger(
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthorizationAuditLogger> logger)
    {
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void PermissionDenied(string permission, string operation, string reasonCode) =>
        _logger.LogInformation(
            "Authorization denied. Event=permission_denied UserId={UserId} Permission={Permission} Operation={Operation} OrganizationId={OrganizationId} ClinicId={ClinicId} CorrelationId={CorrelationId} ReasonCode={ReasonCode}",
            _currentUser.UserId,
            permission,
            operation,
            _currentUser.OrganizationId,
            _currentUser.ClinicId,
            CorrelationId(),
            reasonCode);

    public void CrossTenantDenied(string operation, string reasonCode, Guid? organizationId = null, Guid? clinicId = null) =>
        _logger.LogInformation(
            "Authorization denied. Event=cross_tenant_denied UserId={UserId} Operation={Operation} OrganizationId={OrganizationId} ClinicId={ClinicId} CorrelationId={CorrelationId} ReasonCode={ReasonCode}",
            _currentUser.UserId,
            operation,
            organizationId ?? _currentUser.OrganizationId,
            clinicId ?? _currentUser.ClinicId,
            CorrelationId(),
            reasonCode);

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
}
