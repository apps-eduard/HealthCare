using HealthCare.Application.Authorization;
using HealthCare.Application.MedicalNotes;
using HealthCare.Contracts.MedicalNotes;
using HealthCare.Domain.Identity;
using HealthCare.Domain.MedicalNotes;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.MedicalNotes;

public sealed class MedicalNoteAccessService : IMedicalNoteAccessService
{
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IAuthorizationAuditLogger _auditLogger;

    public MedicalNoteAccessService(
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IAuthorizationAuditLogger auditLogger)
    {
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _auditLogger = auditLogger;
    }

    public bool IsClinicalRole(string? role) =>
        string.Equals(role, AppRoles.Doctor, StringComparison.Ordinal)
        || string.Equals(role, AppRoles.Nurse, StringComparison.Ordinal);

    public void EnsureClinicalStaffForNotes()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            throw MedicalNoteException.AccessDenied();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            _auditLogger.PermissionDenied(Permissions.MedicalNotes.Read, "medical_notes", MedicalNoteErrorCodes.AccessDenied);
            throw MedicalNoteException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership)
        {
            _auditLogger.InactiveMembershipRejected("medical_notes");
            throw MedicalNoteException.ClinicalRoleRequired();
        }

        if (!IsClinicalRole(_currentStaff.Role))
        {
            _auditLogger.PermissionDenied(
                Permissions.MedicalNotes.Read,
                "medical_notes.clinical_role",
                MedicalNoteErrorCodes.ClinicalRoleRequired);
            throw MedicalNoteException.ClinicalRoleRequired();
        }
    }

    public void EnsureClinicScope(Guid organizationId, Guid clinicId)
    {
        if (!_currentStaff.HasActiveMembership
            || _currentStaff.OrganizationId != organizationId
            || _currentStaff.ClinicId != clinicId)
        {
            _auditLogger.CrossTenantDenied(
                "medical_notes",
                MedicalNoteErrorCodes.AccessDenied,
                organizationId,
                clinicId);
            throw MedicalNoteException.NotFound();
        }
    }

    public void EnsureNoteTypeAllowed(MedicalNoteType noteType, string staffRole)
    {
        if (string.Equals(staffRole, AppRoles.Doctor, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(staffRole, AppRoles.Nurse, StringComparison.Ordinal)
            && noteType == MedicalNoteType.Nursing)
        {
            return;
        }

        throw MedicalNoteException.NoteTypeNotAllowed();
    }

    public bool CanAmend(string staffRole) =>
        string.Equals(staffRole, AppRoles.Doctor, StringComparison.Ordinal);
}

/// <summary>
/// Persists metadata-only medical-note audit events. Never accepts note body content.
/// </summary>
public interface IMedicalNoteAuditStore
{
    Task WriteAsync(
        string operation,
        string resultCode,
        Guid? medicalNoteId = null,
        Guid? appointmentId = null,
        Guid? patientId = null,
        Guid? clinicId = null,
        Guid? organizationId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}

public sealed class MedicalNoteAuditStore : IMedicalNoteAuditStore
{
    private readonly Persistence.HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly TimeProvider _time;
    private readonly ILogger<MedicalNoteAuditStore> _logger;

    public MedicalNoteAuditStore(
        Persistence.HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        TimeProvider time,
        ILogger<MedicalNoteAuditStore> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _time = time;
        _logger = logger;
    }

    public async Task WriteAsync(
        string operation,
        string resultCode,
        Guid? medicalNoteId = null,
        Guid? appointmentId = null,
        Guid? patientId = null,
        Guid? clinicId = null,
        Guid? organizationId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var evt = new MedicalNoteAuditEvent
        {
            Id = Guid.NewGuid(),
            MedicalNoteId = medicalNoteId,
            AppointmentId = appointmentId,
            PatientId = patientId,
            ClinicId = clinicId,
            OrganizationId = organizationId,
            ActingUserId = _currentUser.UserId,
            ActingStaffMemberId = _currentStaff.HasActiveMembership ? _currentStaff.StaffMemberId : null,
            Operation = operation,
            ResultCode = resultCode,
            CorrelationId = correlationId,
            CreatedAtUtc = _time.GetUtcNow(),
        };

        _dbContext.MedicalNoteAuditEvents.Add(evt);

        _logger.LogInformation(
            "Medical note audit. Operation={Operation} Result={ResultCode} NoteId={NoteId} AppointmentId={AppointmentId} PatientId={PatientId} ClinicId={ClinicId} OrgId={OrgId} UserId={UserId} StaffId={StaffId} CorrelationId={CorrelationId}",
            operation,
            resultCode,
            medicalNoteId,
            appointmentId,
            patientId,
            clinicId,
            organizationId,
            _currentUser.UserId,
            evt.ActingStaffMemberId,
            correlationId);
    }
}
