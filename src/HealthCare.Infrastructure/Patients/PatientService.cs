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
        var patient = await LoadLinkedActivePatientForUpdateAsync(asNoTracking: true, cancellationToken);
        return Map(patient);
    }

    public async Task<PatientProfileResponse> UpdateCurrentPatientProfileAsync(
        UpdatePatientProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var patient = await LoadLinkedActivePatientForUpdateAsync(asNoTracking: false, cancellationToken);

        if (patient.Version != request.ExpectedVersion)
        {
            _logger.LogInformation(
                "Concurrency conflict. UserId={UserId} PatientId={PatientId} ExpectedVersion={ExpectedVersion} ActualVersion={ActualVersion}",
                _currentUser.UserId,
                patient.Id,
                request.ExpectedVersion,
                patient.Version);
            throw new PatientConcurrencyException();
        }

        if (request.FirstNameSpecified)
        {
            patient.FirstName = NormalizeRequired(request.FirstName!);
        }

        if (request.MiddleNameSpecified)
        {
            patient.MiddleName = NormalizeOptional(request.MiddleName);
        }

        if (request.LastNameSpecified)
        {
            patient.LastName = NormalizeRequired(request.LastName!);
        }

        if (request.DateOfBirthSpecified)
        {
            patient.DateOfBirth = request.DateOfBirth;
        }

        if (request.GenderSpecified)
        {
            patient.Gender = NormalizeOptional(request.Gender);
        }

        if (request.MobileNumberSpecified)
        {
            patient.MobileNumber = NormalizeOptional(request.MobileNumber);
        }

        if (request.PreferredLanguageSpecified)
        {
            patient.PreferredLanguage = NormalizeOptional(request.PreferredLanguage);
        }

        if (request.AddressSpecified)
        {
            patient.Address = NormalizeOptional(request.Address);
        }

        if (request.EmergencyContactSpecified)
        {
            patient.EmergencyContact = NormalizeOptional(request.EmergencyContact);
        }

        patient.Version++;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation(
                "Concurrency conflict. UserId={UserId} PatientId={PatientId}",
                _currentUser.UserId,
                patient.Id);
            throw new PatientConcurrencyException();
        }

        _logger.LogInformation(
            "Patient profile updated. UserId={UserId} PatientId={PatientId} Version={Version}",
            _currentUser.UserId,
            patient.Id,
            patient.Version);

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

    private async Task<Patient> LoadLinkedActivePatientForUpdateAsync(
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.IsInRole(AppRoles.Patient))
        {
            throw AuthorizationException.Forbidden();
        }

        if (!_currentPatient.HasLinkedPatient || _currentPatient.PatientId is null)
        {
            throw AuthorizationException.MissingPatientLinkage();
        }

        var patientId = _currentPatient.PatientId.Value;
        IQueryable<Patient> query = _dbContext.Patients;
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        var patient = await query.SingleOrDefaultAsync(p => p.Id == patientId && p.IsActive, cancellationToken);
        if (patient is null)
        {
            throw AuthorizationException.MissingPatientLinkage();
        }

        return patient;
    }

    private void LogDenial(string reasonCode, Guid patientId)
    {
        _logger.LogInformation(
            "Authorization denied. UserId={UserId} Reason={ReasonCode} Resource={ResourceKey}",
            _currentUser.UserId,
            reasonCode,
            patientId);
    }

    private static string NormalizeRequired(string value) => value.Trim();

    private static string? NormalizeOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
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
            Version = patient.Version,
        };
}
