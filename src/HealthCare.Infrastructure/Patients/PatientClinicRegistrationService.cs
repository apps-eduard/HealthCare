using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Patients;

public sealed class PatientClinicRegistrationService : IPatientClinicRegistrationService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentPatient _currentPatient;
    private readonly IClinicPublicLookup _clinicLookup;
    private readonly ILocalPatientNumberGenerator _numberGenerator;
    private readonly ILogger<PatientClinicRegistrationService> _logger;

    public PatientClinicRegistrationService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentPatient currentPatient,
        IClinicPublicLookup clinicLookup,
        ILocalPatientNumberGenerator numberGenerator,
        ILogger<PatientClinicRegistrationService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentPatient = currentPatient;
        _clinicLookup = clinicLookup;
        _numberGenerator = numberGenerator;
        _logger = logger;
    }

    public async Task<ClinicPatientEnrollmentResponse> RegisterCurrentPatientWithClinicAsync(
        RegisterPatientWithClinicRequest request,
        CancellationToken cancellationToken = default)
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
        var clinic = await _clinicLookup.FindByPublicCodeAsync(request.ClinicCode, cancellationToken);
        if (clinic is null)
        {
            _logger.LogInformation(
                "Clinic registration denied. UserId={UserId} Reason={ReasonCode}",
                _currentUser.UserId,
                PatientErrorCodes.ClinicCodeInvalid);
            throw PatientClinicRegistrationException.InvalidClinicCode();
        }

        if (!clinic.IsActive)
        {
            _logger.LogInformation(
                "Clinic registration denied. UserId={UserId} Reason={ReasonCode}",
                _currentUser.UserId,
                PatientErrorCodes.ClinicInactive);
            throw PatientClinicRegistrationException.ClinicInactive();
        }

        if (clinic.Organization is null || clinic.Organization.Status != OrganizationStatus.Active)
        {
            _logger.LogInformation(
                "Clinic registration denied. UserId={UserId} Reason={ReasonCode}",
                _currentUser.UserId,
                PatientErrorCodes.OrganizationInactive);
            throw PatientClinicRegistrationException.OrganizationInactive();
        }

        var clinicId = clinic.Id;

        var existing = await _dbContext.ClinicPatients
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cp => cp.ClinicId == clinicId && cp.PatientId == patientId,
                cancellationToken);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Existing clinic registration returned. ClinicId={ClinicId} PatientId={PatientId}",
                clinicId,
                patientId);
            return Map(existing, alreadyEnrolled: true);
        }

        var useTransaction = _dbContext.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (useTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            existing = await _dbContext.ClinicPatients
                .SingleOrDefaultAsync(
                    cp => cp.ClinicId == clinicId && cp.PatientId == patientId,
                    cancellationToken);

            if (existing is not null)
            {
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                _logger.LogInformation(
                    "Existing clinic registration returned. ClinicId={ClinicId} PatientId={PatientId}",
                    clinicId,
                    patientId);
                return Map(existing, alreadyEnrolled: true);
            }

            var localNumber = await _numberGenerator.AllocateNextAsync(clinicId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var enrollment = new ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                PatientId = patientId,
                LocalPatientNumber = localNumber,
                Status = ClinicPatientStatus.Active,
                Version = 0,
                RegisteredAtUtc = now,
                UpdatedAtUtc = now,
            };

            _dbContext.ClinicPatients.Add(enrollment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Patient clinic registration created. ClinicPatientId={ClinicPatientId} ClinicId={ClinicId} PatientId={PatientId}",
                enrollment.Id,
                clinicId,
                patientId);

            return Map(enrollment, alreadyEnrolled: false);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            existing = await _dbContext.ClinicPatients
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    cp => cp.ClinicId == clinicId && cp.PatientId == patientId,
                    cancellationToken);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Existing clinic registration returned. ClinicId={ClinicId} PatientId={PatientId}",
                    clinicId,
                    patientId);
                return Map(existing, alreadyEnrolled: true);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static ClinicPatientEnrollmentResponse Map(ClinicPatient entity, bool alreadyEnrolled) =>
        new()
        {
            ClinicPatientId = entity.Id,
            ClinicId = entity.ClinicId,
            PatientId = entity.PatientId,
            LocalPatientNumber = entity.LocalPatientNumber,
            Status = entity.Status.ToString(),
            AlreadyEnrolled = alreadyEnrolled,
        };
}
