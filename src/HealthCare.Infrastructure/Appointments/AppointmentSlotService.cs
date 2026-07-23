using HealthCare.Application.Appointments;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class AppointmentSlotService : IAppointmentSlotService
{
    private static readonly AppointmentStatus[] OccupyingStatuses =
    [
        AppointmentStatus.Requested,
        AppointmentStatus.Confirmed,
        AppointmentStatus.CheckedIn,
        AppointmentStatus.InProgress,
    ];

    private readonly HealthCareDbContext _dbContext;
    private readonly IClinicPublicLookup _clinicLookup;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentSlotService> _logger;

    public AppointmentSlotService(
        HealthCareDbContext dbContext,
        IClinicPublicLookup clinicLookup,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<AppointmentSlotService> logger)
    {
        _dbContext = dbContext;
        _clinicLookup = clinicLookup;
        _timeZones = timeZones;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(
        string clinicCode,
        Guid staffMemberId,
        AvailableSlotsQuery query,
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

        var doctor = await _dbContext.StaffMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == staffMemberId
                     && s.ClinicId == clinic.Id
                     && s.IsActive
                     && s.Role == AppRoles.Doctor,
                cancellationToken);

        if (doctor is null)
        {
            throw AvailabilityException.DoctorNotFound();
        }

        var slots = await BuildSlotsForDateAsync(
            clinic.Id,
            clinic.TimeZoneId,
            doctor.Id,
            query.Date,
            query.DurationMinutes,
            cancellationToken);

        _logger.LogInformation(
            "Available slots queried. ClinicId={ClinicId} DoctorStaffMemberId={DoctorStaffMemberId} Date={Date} ResultCount={ResultCount}",
            clinic.Id,
            doctor.Id,
            query.Date,
            slots.Count);

        return slots;
    }

    public async Task EnsureSlotIsBookableAsync(
        Guid clinicId,
        Guid doctorStaffMemberId,
        DateTimeOffset startUtc,
        int durationMinutes,
        Guid? excludeAppointmentId = null,
        CancellationToken cancellationToken = default)
    {
        var clinic = await _dbContext.Clinics
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken)
            ?? throw AppointmentException.InactiveClinic();

        if (!clinic.IsActive)
        {
            throw AppointmentException.InactiveClinic();
        }

        if (!AvailabilitySlotRules.IsValidDuration(durationMinutes))
        {
            throw AvailabilityException.InvalidSlotDuration();
        }

        var now = _timeProvider.GetUtcNow();
        if (startUtc <= now)
        {
            throw AppointmentException.InvalidTime();
        }

        var localDate = _timeZones.GetClinicDate(startUtc, clinic.TimeZoneId);
        var localTime = _timeZones.GetClinicTime(startUtc, clinic.TimeZoneId);

        var windows = await ResolveEffectiveWindowsAsync(
            doctorStaffMemberId,
            localDate,
            cancellationToken);

        if (windows.Count == 0)
        {
            _logger.LogInformation(
                "Booking rejected by availability. ClinicId={ClinicId} DoctorStaffMemberId={DoctorStaffMemberId} Reason={Reason}",
                clinicId,
                doctorStaffMemberId,
                "outside_availability");
            throw AvailabilityException.OutsideAvailability();
        }

        DoctorAvailability? matched = null;
        foreach (var window in windows)
        {
            if (localTime < window.StartLocalTime || localTime >= window.EndLocalTime)
            {
                continue;
            }

            if (window.SlotDurationMinutes != durationMinutes)
            {
                throw AvailabilityException.InvalidSlotDuration();
            }

            if (!AvailabilitySlotRules.IsOnSlotBoundary(localTime, window.StartLocalTime, window.SlotDurationMinutes))
            {
                throw AvailabilityException.SlotUnavailable();
            }

            if (!AvailabilitySlotRules.FitsWithinWindow(localTime, durationMinutes, window.EndLocalTime))
            {
                throw AvailabilityException.OutsideAvailability();
            }

            matched = window;
            break;
        }

        if (matched is null)
        {
            // Could be blocked by exception emptying windows, or not on a window.
            var blocked = await IsBlockedByUnavailableExceptionAsync(
                doctorStaffMemberId,
                localDate,
                localTime,
                durationMinutes,
                cancellationToken);
            if (blocked)
            {
                throw AvailabilityException.BlockedByException();
            }

            throw AvailabilityException.OutsideAvailability();
        }

        var endUtc = startUtc.AddMinutes(durationMinutes);
        var candidates = await _dbContext.Appointments
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.DoctorStaffMemberId == doctorStaffMemberId
                        && OccupyingStatuses.Contains(a.Status)
                        && (!excludeAppointmentId.HasValue || a.Id != excludeAppointmentId.Value)
                        && a.AppointmentDateUtc < endUtc)
            .Select(a => new { a.AppointmentDateUtc, a.DurationMinutes })
            .ToListAsync(cancellationToken);

        if (candidates.Any(a => a.AppointmentDateUtc.AddMinutes(a.DurationMinutes) > startUtc))
        {
            throw AvailabilityException.SlotUnavailable();
        }
    }

    private async Task<IReadOnlyList<AvailableSlotResponse>> BuildSlotsForDateAsync(
        Guid clinicId,
        string timeZoneId,
        Guid doctorId,
        DateOnly date,
        int? requestedDuration,
        CancellationToken cancellationToken)
    {
        var windows = await ResolveEffectiveWindowsAsync(doctorId, date, cancellationToken);
        if (windows.Count == 0)
        {
            return Array.Empty<AvailableSlotResponse>();
        }

        var now = _timeProvider.GetUtcNow();
        var dayStartUtc = _timeZones.ToUtc(date, TimeOnly.MinValue, timeZoneId);
        var dayEndUtc = _timeZones.ToUtc(date.AddDays(1), TimeOnly.MinValue, timeZoneId);

        var dayCandidates = await _dbContext.Appointments
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.DoctorStaffMemberId == doctorId
                        && OccupyingStatuses.Contains(a.Status)
                        && a.AppointmentDateUtc < dayEndUtc)
            .Select(a => new { a.AppointmentDateUtc, a.DurationMinutes })
            .ToListAsync(cancellationToken);

        var appointments = dayCandidates
            .Where(a => a.AppointmentDateUtc.AddMinutes(a.DurationMinutes) > dayStartUtc)
            .ToList();

        var results = new List<AvailableSlotResponse>();
        foreach (var window in windows)
        {
            if (requestedDuration.HasValue && requestedDuration.Value != window.SlotDurationMinutes)
            {
                continue;
            }

            foreach (var startLocal in AvailabilitySlotRules.GenerateSlotStarts(
                         window.StartLocalTime,
                         window.EndLocalTime,
                         window.SlotDurationMinutes))
            {
                var startUtc = _timeZones.ToUtc(date, startLocal, timeZoneId);
                if (startUtc <= now)
                {
                    continue;
                }

                var endUtc = startUtc.AddMinutes(window.SlotDurationMinutes);
                if (appointments.Any(a =>
                        a.AppointmentDateUtc < endUtc
                        && a.AppointmentDateUtc.AddMinutes(a.DurationMinutes) > startUtc))
                {
                    continue;
                }

                var startLocalOffset = _timeZones.ToClinicLocal(startUtc, timeZoneId);
                var endLocalOffset = _timeZones.ToClinicLocal(endUtc, timeZoneId);
                results.Add(new AvailableSlotResponse
                {
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    StartLocal = startLocalOffset.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    EndLocal = endLocalOffset.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    DurationMinutes = window.SlotDurationMinutes,
                    TimeZoneId = timeZoneId,
                });
            }
        }

        return results.OrderBy(s => s.StartUtc).ToList();
    }

    private async Task<List<DoctorAvailability>> ResolveEffectiveWindowsAsync(
        Guid doctorId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var weekly = await _dbContext.DoctorAvailabilities
            .AsNoTracking()
            .Where(a => a.DoctorStaffMemberId == doctorId
                        && a.IsActive
                        && a.DayOfWeek == date.DayOfWeek
                        && a.EffectiveFrom <= date
                        && (a.EffectiveTo == null || a.EffectiveTo >= date))
            .ToListAsync(cancellationToken);

        var exceptions = await _dbContext.DoctorAvailabilityExceptions
            .AsNoTracking()
            .Where(e => e.DoctorStaffMemberId == doctorId && e.Date == date)
            .ToListAsync(cancellationToken);

        if (exceptions.Any(e => e.ExceptionType == AvailabilityExceptionType.UnavailableFullDay))
        {
            return [];
        }

        var custom = exceptions
            .Where(e => e.ExceptionType == AvailabilityExceptionType.CustomAvailableRange
                        && e.StartLocalTime.HasValue
                        && e.EndLocalTime.HasValue)
            .ToList();

        if (custom.Count > 0)
        {
            return custom.Select(e => new DoctorAvailability
            {
                Id = e.Id,
                DoctorStaffMemberId = doctorId,
                DayOfWeek = date.DayOfWeek,
                StartLocalTime = e.StartLocalTime!.Value,
                EndLocalTime = e.EndLocalTime!.Value,
                SlotDurationMinutes = weekly.FirstOrDefault()?.SlotDurationMinutes
                                      ?? AvailabilitySlotRules.MinSlotDurationMinutes,
                EffectiveFrom = date,
                EffectiveTo = date,
                IsActive = true,
            }).ToList();
        }

        var unavailable = exceptions
            .Where(e => e.ExceptionType == AvailabilityExceptionType.UnavailableRange
                        && e.StartLocalTime.HasValue
                        && e.EndLocalTime.HasValue)
            .ToList();

        if (unavailable.Count == 0)
        {
            return weekly;
        }

        var trimmed = new List<DoctorAvailability>();
        foreach (var window in weekly)
        {
            trimmed.AddRange(SubtractUnavailable(window, unavailable));
        }

        return trimmed;
    }

    private static IEnumerable<DoctorAvailability> SubtractUnavailable(
        DoctorAvailability window,
        List<DoctorAvailabilityException> unavailable)
    {
        var segments = new List<(TimeOnly Start, TimeOnly End)> { (window.StartLocalTime, window.EndLocalTime) };
        foreach (var block in unavailable.OrderBy(u => u.StartLocalTime))
        {
            var next = new List<(TimeOnly Start, TimeOnly End)>();
            foreach (var (start, end) in segments)
            {
                var bStart = block.StartLocalTime!.Value;
                var bEnd = block.EndLocalTime!.Value;
                if (!AvailabilitySlotRules.TimeRangesOverlap(start, end, bStart, bEnd))
                {
                    next.Add((start, end));
                    continue;
                }

                if (start < bStart)
                {
                    next.Add((start, bStart < end ? bStart : end));
                }

                if (bEnd < end)
                {
                    next.Add((bEnd > start ? bEnd : start, end));
                }
            }

            segments = next;
        }

        foreach (var (start, end) in segments)
        {
            if (start >= end)
            {
                continue;
            }

            yield return new DoctorAvailability
            {
                Id = window.Id,
                DoctorStaffMemberId = window.DoctorStaffMemberId,
                DayOfWeek = window.DayOfWeek,
                StartLocalTime = start,
                EndLocalTime = end,
                SlotDurationMinutes = window.SlotDurationMinutes,
                EffectiveFrom = window.EffectiveFrom,
                EffectiveTo = window.EffectiveTo,
                IsActive = true,
            };
        }
    }

    private async Task<bool> IsBlockedByUnavailableExceptionAsync(
        Guid doctorId,
        DateOnly date,
        TimeOnly start,
        int durationMinutes,
        CancellationToken cancellationToken)
    {
        var endMinutes = start.Hour * 60 + start.Minute + durationMinutes;
        var end = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(endMinutes));

        return await _dbContext.DoctorAvailabilityExceptions
            .AsNoTracking()
            .AnyAsync(
                e => e.DoctorStaffMemberId == doctorId
                     && e.Date == date
                     && (e.ExceptionType == AvailabilityExceptionType.UnavailableFullDay
                         || (e.ExceptionType == AvailabilityExceptionType.UnavailableRange
                             && e.StartLocalTime != null
                             && e.EndLocalTime != null
                             && e.StartLocalTime < end
                             && start < e.EndLocalTime)),
                cancellationToken);
    }
}
