using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
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
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IAuthorizationAuditLogger _audit;

    public DoctorDirectoryService(
        HealthCareDbContext dbContext,
        IClinicPublicLookup clinicLookup,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IAuthorizationAuditLogger audit)
    {
        _dbContext = dbContext;
        _clinicLookup = clinicLookup;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _audit = audit;
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

        return await ListActiveDoctorsInClinicAsync(clinic.Id, clinic.Slug, clinic.Specialty, clinic.TimeZoneId, cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicDoctorResponse>> ListDoctorsByClinicIdAsync(
        Guid clinicId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        var clinic = await _dbContext.Clinics
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken);

        if (clinic is null || !clinic.IsActive)
        {
            throw AuthorizationException.ClinicAccessDenied();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("staff_clinic_doctors", clinic.OrganizationId, clinic.Id);
        }
        else
        {
            if (!_currentStaff.HasActiveMembership)
            {
                throw AuthorizationException.MissingStaffMembership();
            }

            if (_currentStaff.Role == AppRoles.OrganizationAdmin)
            {
                if (clinic.OrganizationId != _currentStaff.OrganizationId)
                {
                    _audit.CrossTenantDenied(
                        "staff_clinic_doctors_denied",
                        Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                        _currentStaff.OrganizationId,
                        clinicId);
                    throw AuthorizationException.ClinicAccessDenied();
                }
            }
            else if (clinic.Id != _currentStaff.ClinicId
                     || clinic.OrganizationId != _currentStaff.OrganizationId)
            {
                _audit.CrossTenantDenied(
                    "staff_clinic_doctors_denied",
                    Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                    _currentStaff.OrganizationId,
                    clinicId);
                throw AuthorizationException.ClinicAccessDenied();
            }
        }

        _audit.AvailabilityOperation(
            "staff_clinic_doctors_list",
            "succeeded",
            clinic.OrganizationId,
            clinic.Id,
            doctorStaffMemberId: null);

        return await ListActiveDoctorsInClinicAsync(
            clinic.Id,
            clinic.Slug,
            clinic.Specialty,
            clinic.TimeZoneId,
            cancellationToken);
    }

    private async Task<IReadOnlyList<ClinicDoctorResponse>> ListActiveDoctorsInClinicAsync(
        Guid clinicId,
        string clinicSlug,
        string? specialty,
        string? timeZoneId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var doctors = await _dbContext.StaffMembers
            .AsNoTracking()
            .Where(s => s.ClinicId == clinicId
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
                Specialty = specialty,
                ClinicCode = clinicSlug,
                ClinicId = clinicId,
                AcceptsBookings = d.HasAvailability,
                ClinicTimeZoneId = timeZoneId,
            })
            .OrderBy(d => d.DisplayName)
            .ToList();
    }
}
