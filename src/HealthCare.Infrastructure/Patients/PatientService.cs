using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Patients;

public sealed class PatientService : IPatientService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentPatient _currentPatient;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ILogger<PatientService> _logger;

    public PatientService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        ICurrentPatient currentPatient,
        ITenantAccessService tenantAccess,
        ILogger<PatientService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _currentPatient = currentPatient;
        _tenantAccess = tenantAccess;
        _logger = logger;
    }

    public async Task<PatientProfileResponse> GetCurrentPatientProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.IsInRole(AppRoles.Patient))
        {
            throw AuthorizationException.Forbidden();
        }

        if (!_currentPatient.HasLinkedPatient || _currentPatient.PatientId is null)
        {
            throw AuthorizationException.MissingPatientLinkage();
        }

        // Always use server-resolved PatientId — ignore any client-supplied id.
        var patientId = _currentPatient.PatientId.Value;
        var patient = await _dbContext.Patients
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == patientId && p.IsActive, cancellationToken);

        if (patient is null)
        {
            throw AuthorizationException.MissingPatientLinkage();
        }

        return Map(patient);
    }

    public async Task<PatientProfileResponse> GetPatientByIdAsync(
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        await EnsureCanAccessPatientRecordAsync(patientId, bypass, cancellationToken);

        var patient = await _dbContext.Patients
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        if (patient is null)
        {
            // Do not reveal whether the patient exists in another tenant.
            throw AuthorizationException.PatientSelfScopeDenied();
        }

        return Map(patient);
    }

    public async Task EnsureCanAccessPatientRecordAsync(
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _logger.LogInformation(
                "PLATFORM_ADMIN explicit patient bypass. UserId={UserId} PatientId={PatientId}",
                _currentUser.UserId,
                patientId);
            return;
        }

        if (_currentUser.IsInRole(AppRoles.Patient))
        {
            _tenantAccess.EnsureCanAccessPatient(patientId);
            return;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            LogDenial("patient_access_denied_no_staff", patientId);
            throw AuthorizationException.PatientSelfScopeDenied();
        }

        var clinicLinks = await (
                from cp in _dbContext.ClinicPatients.AsNoTracking()
                join clinic in _dbContext.Clinics.AsNoTracking() on cp.ClinicId equals clinic.Id
                where cp.PatientId == patientId && cp.Status == ClinicPatientStatus.Active
                select new { cp.ClinicId, clinic.OrganizationId })
            .ToListAsync(cancellationToken);

        if (clinicLinks.Count == 0)
        {
            LogDenial("patient_access_denied_no_clinic_link", patientId);
            throw AuthorizationException.PatientSelfScopeDenied();
        }

        var staffRole = _currentStaff.Role;
        if (staffRole == AppRoles.OrganizationAdmin)
        {
            var orgId = _currentStaff.OrganizationId;
            if (clinicLinks.Any(l => l.OrganizationId == orgId))
            {
                return;
            }

            LogDenial("patient_organization_access_denied", patientId);
            throw AuthorizationException.OrganizationAccessDenied();
        }

        var clinicId = _currentStaff.ClinicId;
        if (clinicLinks.Any(l => l.ClinicId == clinicId))
        {
            return;
        }

        LogDenial("patient_clinic_access_denied", patientId);
        throw AuthorizationException.ClinicAccessDenied();
    }

    private void LogDenial(string reasonCode, Guid patientId)
    {
        _logger.LogInformation(
            "Authorization denied. UserId={UserId} Reason={ReasonCode} Resource={ResourceKey}",
            _currentUser.UserId,
            reasonCode,
            patientId);
    }

    private static PatientProfileResponse Map(Patient patient) =>
        new()
        {
            Id = patient.Id,
            FirstName = patient.FirstName,
            MiddleName = patient.MiddleName,
            LastName = patient.LastName,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            MobileNumber = patient.MobileNumber,
            PreferredLanguage = patient.PreferredLanguage,
            Address = patient.Address,
            EmergencyContact = patient.EmergencyContact,
            IsActive = patient.IsActive,
            LinkedUserId = patient.UserId,
        };
}
