using HealthCare.Application.Appointments;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class ClinicAppointmentSummaryBuilder : IClinicAppointmentSummaryBuilder
{
    private readonly HealthCareDbContext _dbContext;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly ILogger<ClinicAppointmentSummaryBuilder> _logger;

    public ClinicAppointmentSummaryBuilder(
        HealthCareDbContext dbContext,
        IClinicTimeZoneConverter timeZones,
        ILogger<ClinicAppointmentSummaryBuilder> logger)
    {
        _dbContext = dbContext;
        _timeZones = timeZones;
        _logger = logger;
    }

    public async Task<ClinicAppointmentSummaryResponse> BuildAsync(
        Guid clinicId,
        DateOnly summaryDate,
        CancellationToken cancellationToken = default)
    {
        var clinic = await _dbContext.Clinics
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken)
            ?? throw AppointmentSummaryException.NotFound();

        var organization = await _dbContext.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == clinic.OrganizationId, cancellationToken);

        if (organization is null || organization.Status != OrganizationStatus.Active || !clinic.IsActive)
        {
            throw AppointmentSummaryException.NotFound();
        }

        var dayStartUtc = _timeZones.ToUtc(summaryDate, TimeOnly.MinValue, clinic.TimeZoneId);
        var dayEndUtc = _timeZones.ToUtc(summaryDate.AddDays(1), TimeOnly.MinValue, clinic.TimeZoneId);

        var appointments = await _dbContext.Appointments
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.AppointmentDateUtc >= dayStartUtc
                        && a.AppointmentDateUtc < dayEndUtc)
            .OrderBy(a => a.AppointmentDateUtc)
            .ThenBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.DoctorStaffMemberId,
                a.AppointmentDateUtc,
                a.Status,
            })
            .ToListAsync(cancellationToken);

        var doctorIds = appointments.Select(a => a.DoctorStaffMemberId).Distinct().ToList();
        var doctors = await _dbContext.StaffMembers
            .AsNoTracking()
            .Where(s => doctorIds.Contains(s.Id))
            .Select(s => new { s.Id, s.JobTitle, s.IsActive })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        string DoctorName(Guid doctorId)
        {
            if (!doctors.TryGetValue(doctorId, out var doctor) || !doctor.IsActive)
            {
                return "Unassigned";
            }

            return string.IsNullOrWhiteSpace(doctor.JobTitle) ? "Doctor" : doctor.JobTitle!;
        }

        var items = appointments.Select(a => new ClinicAppointmentSummaryItem
        {
            AppointmentId = a.Id,
            LocalTime = _timeZones.ToClinicLocal(a.AppointmentDateUtc, clinic.TimeZoneId).ToString("HH:mm"),
            Status = a.Status.ToString(),
            DoctorDisplayName = DoctorName(a.DoctorStaffMemberId),
        }).ToList();

        var byDoctor = appointments
            .GroupBy(a => a.DoctorStaffMemberId)
            .Select(g =>
            {
                var active = doctors.TryGetValue(g.Key, out var d) && d.IsActive;
                return new ClinicAppointmentSummaryDoctorGroup
                {
                    DoctorStaffMemberId = active ? g.Key : null,
                    DoctorDisplayName = DoctorName(g.Key),
                    Count = g.Count(),
                };
            })
            .OrderBy(g => g.DoctorDisplayName)
            .ThenBy(g => g.DoctorStaffMemberId)
            .ToList();

        var first = appointments.FirstOrDefault();
        var last = appointments.LastOrDefault();

        var summary = new ClinicAppointmentSummaryResponse
        {
            ClinicId = clinic.Id,
            OrganizationId = clinic.OrganizationId,
            ClinicCode = clinic.Slug,
            ClinicName = clinic.Name,
            TimeZoneId = clinic.TimeZoneId,
            SummaryDate = summaryDate.ToString("yyyy-MM-dd"),
            TotalAppointments = appointments.Count,
            Requested = appointments.Count(a => a.Status == AppointmentStatus.Requested),
            Confirmed = appointments.Count(a => a.Status == AppointmentStatus.Confirmed),
            CheckedIn = appointments.Count(a => a.Status == AppointmentStatus.CheckedIn),
            InProgress = appointments.Count(a => a.Status == AppointmentStatus.InProgress),
            Completed = appointments.Count(a => a.Status == AppointmentStatus.Completed),
            NoShow = appointments.Count(a => a.Status == AppointmentStatus.NoShow),
            CancelledByPatient = appointments.Count(a => a.Status == AppointmentStatus.CancelledByPatient),
            CancelledByClinic = appointments.Count(a => a.Status == AppointmentStatus.CancelledByClinic),
            UnassignedAppointments = appointments.Count(a =>
                !doctors.TryGetValue(a.DoctorStaffMemberId, out var d) || !d.IsActive),
            FirstAppointmentUtc = first?.AppointmentDateUtc,
            LastAppointmentUtc = last?.AppointmentDateUtc,
            FirstAppointmentLocal = first is null
                ? null
                : _timeZones.ToClinicLocal(first.AppointmentDateUtc, clinic.TimeZoneId).ToString("yyyy-MM-dd HH:mm"),
            LastAppointmentLocal = last is null
                ? null
                : _timeZones.ToClinicLocal(last.AppointmentDateUtc, clinic.TimeZoneId).ToString("yyyy-MM-dd HH:mm"),
            ByDoctor = byDoctor,
            Appointments = items,
        };

        _logger.LogInformation(
            "Appointment summary generated. ClinicId={ClinicId} OrganizationId={OrganizationId} SummaryDate={SummaryDate} AppointmentCount={AppointmentCount}",
            clinic.Id,
            clinic.OrganizationId,
            summary.SummaryDate,
            summary.TotalAppointments);

        return summary;
    }
}
