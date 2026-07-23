using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Patients;

public sealed class PatientAccountLinker : IPatientAccountLinker
{
    private readonly HealthCareDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PatientAccountLinker> _logger;

    public PatientAccountLinker(
        HealthCareDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ILogger<PatientAccountLinker> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task LinkUserToPatientAsync(Guid userId, Guid patientId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw PatientLinkageException.UserNotFound();
        }

        if (!user.IsActive)
        {
            throw PatientLinkageException.UserInactive();
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(AppRoles.Patient, StringComparer.Ordinal))
        {
            throw PatientLinkageException.UserNotPatientRole();
        }

        var isStaff = await _dbContext.StaffMembers
            .AsNoTracking()
            .AnyAsync(s => s.UserId == userId, cancellationToken);
        if (isStaff)
        {
            throw PatientLinkageException.UserIsStaff();
        }

        var staffRoles = roles.Where(r =>
            r is AppRoles.OrganizationAdmin
                or AppRoles.ClinicAdmin
                or AppRoles.Doctor
                or AppRoles.Nurse
                or AppRoles.Receptionist
                or AppRoles.PlatformAdmin).ToArray();
        if (staffRoles.Length > 0)
        {
            throw PatientLinkageException.UserIsStaff();
        }

        var patient = await _dbContext.Patients
            .SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);
        if (patient is null)
        {
            throw PatientLinkageException.PatientNotFound();
        }

        if (patient.UserId is not null && patient.UserId != userId)
        {
            throw PatientLinkageException.PatientAlreadyLinked();
        }

        if (patient.UserId == userId)
        {
            return;
        }

        var existingForUser = await _dbContext.Patients
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId && p.Id != patientId, cancellationToken);
        if (existingForUser)
        {
            throw PatientLinkageException.DuplicateUserLinkage();
        }

        patient.UserId = userId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Linked patient profile. PatientId={PatientId} UserId={UserId}",
            patientId,
            userId);
    }
}
