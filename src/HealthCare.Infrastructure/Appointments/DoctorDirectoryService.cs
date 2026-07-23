using HealthCare.Application.Appointments;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.Infrastructure.Appointments;

public sealed class DoctorDirectoryService : IDoctorDirectoryService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly IClinicPublicLookup _clinicLookup;

    public DoctorDirectoryService(HealthCareDbContext dbContext, IClinicPublicLookup clinicLookup)
    {
        _dbContext = dbContext;
        _clinicLookup = clinicLookup;
    }

    public async Task<IReadOnlyList<ClinicDoctorResponse>> ListDoctorsByClinicCodeAsync(
        string clinicCode,
        CancellationToken cancellationToken = default)
    {
        var clinic = await _clinicLookup.FindByPublicCodeAsync(clinicCode.Trim(), cancellationToken);
        if (clinic is null || !clinic.IsActive)
        {
            throw AppointmentException.InactiveClinic();
        }

        var organization = await _dbContext.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == clinic.OrganizationId, cancellationToken);

        if (organization is null || organization.Status != OrganizationStatus.Active)
        {
            throw AppointmentException.InactiveClinic();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var doctors = await _dbContext.StaffMembers
            .AsNoTracking()
            .Where(s => s.ClinicId == clinic.Id
                        && s.IsActive
                        && s.Role == AppRoles.Doctor)
            .Select(s => new
            {
                s.Id,
                s.JobTitle,
                HasAvailability = _dbContext.DoctorAvailabilities.Any(a =>
                    a.DoctorStaffMemberId == s.Id
                    && a.IsActive
                    && a.EffectiveFrom <= today
                    && (a.EffectiveTo == null || a.EffectiveTo >= today)),
            })
            .ToListAsync(cancellationToken);

        return doctors
            .Select(d => new ClinicDoctorResponse
            {
                StaffMemberId = d.Id,
                DisplayName = string.IsNullOrWhiteSpace(d.JobTitle) ? "Doctor" : d.JobTitle!,
                Specialty = clinic.Specialty,
                ClinicCode = clinic.Slug,
                AcceptsBookings = d.HasAvailability,
            })
            .OrderBy(d => d.DisplayName)
            .ToList();
    }
}
