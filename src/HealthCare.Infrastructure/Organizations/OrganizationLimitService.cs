using HealthCare.Application.Clinics;
using HealthCare.Application.Organizations;
using HealthCare.Application.Staff;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Organizations;

public sealed class OrganizationLimitService : IOrganizationLimitService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly OrganizationLimitsOptions _limits;
    private readonly TimeProvider _timeProvider;

    public OrganizationLimitService(
        HealthCareDbContext dbContext,
        IOptions<OrganizationLimitsOptions> limits,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _limits = limits.Value;
        _timeProvider = timeProvider;
    }

    public async Task EnsureClinicCapacityAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(organizationId, clinicId: null, cancellationToken);
        if (snapshot.ClinicLimitReached)
        {
            throw ClinicManagementException.LimitReached();
        }
    }

    public async Task EnsureStaffCapacityAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(organizationId, clinicId: null, cancellationToken);
        if (snapshot.StaffLimitReached)
        {
            throw StaffManagementException.LimitReached();
        }
    }

    public async Task<OrganizationLimitSnapshot> GetSnapshotAsync(
        Guid organizationId,
        Guid? clinicId = null,
        CancellationToken cancellationToken = default)
    {
        var org = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new { o.Id, o.Name, o.MaxClinics, o.MaxStaff })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Organization was not found.");

        var maxClinics = org.MaxClinics ?? Math.Max(1, _limits.DefaultMaxClinics);
        var maxStaff = org.MaxStaff ?? Math.Max(1, _limits.DefaultMaxStaff);
        var warningPercent = Math.Clamp(_limits.WarningThresholdPercent, 1, 100);

        var clinicQuery = _dbContext.Clinics.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId);
        if (clinicId.HasValue)
        {
            clinicQuery = clinicQuery.Where(c => c.Id == clinicId.Value);
        }

        var clinicCount = await clinicQuery.CountAsync(cancellationToken);
        var activeClinicCount = await clinicQuery.CountAsync(c => c.IsActive, cancellationToken);

        // Capacity is always organization-wide (not clinic-filtered).
        var orgClinicCount = clinicId.HasValue
            ? await _dbContext.Clinics.AsNoTracking().CountAsync(c => c.OrganizationId == organizationId, cancellationToken)
            : clinicCount;

        var staffBase = _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId);
        var staffCount = await staffBase.CountAsync(cancellationToken);
        var activeStaffCount = await staffBase.CountAsync(s => s.IsActive, cancellationToken);
        var activeDoctorCount = await staffBase.CountAsync(
            s => s.IsActive && s.Role == AppRoles.Doctor,
            cancellationToken);

        if (clinicId.HasValue)
        {
            activeDoctorCount = await staffBase.CountAsync(
                s => s.IsActive && s.Role == AppRoles.Doctor && s.ClinicId == clinicId.Value,
                cancellationToken);
        }

        IQueryable<Guid> clinicIdsQuery = _dbContext.Clinics.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => c.Id);
        if (clinicId.HasValue)
        {
            clinicIdsQuery = clinicIdsQuery.Where(id => id == clinicId.Value);
        }

        var patientClinicIds = await clinicIdsQuery.ToListAsync(cancellationToken);
        var patientCount = patientClinicIds.Count == 0
            ? 0
            : await _dbContext.ClinicPatients.AsNoTracking()
                .Where(cp => patientClinicIds.Contains(cp.ClinicId))
                .Select(cp => cp.PatientId)
                .Distinct()
                .CountAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthEnd = monthStart.AddMonths(1);

        var appointmentQuery = _dbContext.Appointments.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId
                        && a.AppointmentDateUtc >= monthStart
                        && a.AppointmentDateUtc < monthEnd);
        if (clinicId.HasValue)
        {
            appointmentQuery = appointmentQuery.Where(a => a.ClinicId == clinicId.Value);
        }

        var monthlyAppointments = await appointmentQuery.CountAsync(cancellationToken);

        var remainingClinics = Math.Max(0, maxClinics - orgClinicCount);
        var remainingStaff = Math.Max(0, maxStaff - staffCount);
        var clinicReached = orgClinicCount >= maxClinics;
        var staffReached = staffCount >= maxStaff;
        var clinicWarning = !clinicReached && orgClinicCount * 100 >= maxClinics * warningPercent;
        var staffWarning = !staffReached && staffCount * 100 >= maxStaff * warningPercent;

        return new OrganizationLimitSnapshot
        {
            OrganizationId = org.Id,
            OrganizationName = org.Name,
            ClinicCount = clinicId.HasValue ? clinicCount : orgClinicCount,
            ActiveClinicCount = activeClinicCount,
            StaffCount = staffCount,
            ActiveStaffCount = activeStaffCount,
            ActiveDoctorCount = activeDoctorCount,
            PatientCount = patientCount,
            MonthlyAppointmentCount = monthlyAppointments,
            MaxClinics = maxClinics,
            MaxStaff = maxStaff,
            RemainingClinicCapacity = remainingClinics,
            RemainingStaffCapacity = remainingStaff,
            ClinicLimitWarning = clinicWarning,
            StaffLimitWarning = staffWarning,
            ClinicLimitReached = clinicReached,
            StaffLimitReached = staffReached,
            WarningThresholdPercent = warningPercent,
        };
    }
}
