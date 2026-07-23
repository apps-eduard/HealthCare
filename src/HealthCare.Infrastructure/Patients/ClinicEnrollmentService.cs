using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Patients;

public sealed class ClinicEnrollmentService : IClinicEnrollmentService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ITenantAccessService _tenantAccess;
    private readonly ILocalPatientNumberGenerator _numberGenerator;
    private readonly ILogger<ClinicEnrollmentService> _logger;

    public ClinicEnrollmentService(
        HealthCareDbContext dbContext,
        ITenantAccessService tenantAccess,
        ILocalPatientNumberGenerator numberGenerator,
        ILogger<ClinicEnrollmentService> logger)
    {
        _dbContext = dbContext;
        _tenantAccess = tenantAccess;
        _numberGenerator = numberGenerator;
        _logger = logger;
    }

    public async Task<ClinicPatientEnrollmentResponse> EnrollAsync(
        Guid clinicId,
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        _tenantAccess.EnsureCanAccessClinic(clinicId, bypass);

        var clinic = await _dbContext.Clinics
            .AsNoTracking()
            .Include(c => c.Organization)
            .SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken);

        if (clinic is null || !clinic.IsActive
            || clinic.Organization is null
            || clinic.Organization.Status != OrganizationStatus.Active)
        {
            throw AuthorizationException.ClinicAccessDenied();
        }

        var patient = await _dbContext.Patients
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == patientId && p.IsActive, cancellationToken);

        if (patient is null)
        {
            throw AuthorizationException.PatientSelfScopeDenied();
        }

        var existing = await _dbContext.ClinicPatients
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cp => cp.ClinicId == clinicId && cp.PatientId == patientId,
                cancellationToken);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Duplicate enrollment detected. ClinicId={ClinicId} PatientId={PatientId}",
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
                    "Duplicate enrollment detected. ClinicId={ClinicId} PatientId={PatientId}",
                    clinicId,
                    patientId);
                return Map(existing, alreadyEnrolled: true);
            }

            var localNumber = await _numberGenerator.AllocateNextAsync(clinicId, cancellationToken);
            var enrollment = new ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                PatientId = patientId,
                LocalPatientNumber = localNumber,
                Status = ClinicPatientStatus.Active,
            };

            _dbContext.ClinicPatients.Add(enrollment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Clinic enrollment created. ClinicPatientId={ClinicPatientId} ClinicId={ClinicId} PatientId={PatientId}",
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
                    "Duplicate enrollment detected. ClinicId={ClinicId} PatientId={PatientId}",
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
