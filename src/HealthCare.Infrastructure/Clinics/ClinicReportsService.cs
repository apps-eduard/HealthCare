using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Clinics;

/// <summary>
/// Clinic-scoped operational reports for CLINIC_ADMIN (and PLATFORM_ADMIN with explicit bypass).
/// Date windows use clinic IANA timezone. No CSV export. No patient-level rows.
/// </summary>
public sealed class ClinicReportsService : IClinicReportsService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicReportsService> _logger;

    public ClinicReportsService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<ClinicReportsService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _timeZones = timeZones;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ClinicAppointmentReportResponse> GetAppointmentsAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(query, bypass, cancellationToken);
        var appointments = await LoadAppointmentsAsync(resolved, cancellationToken);

        var total = appointments.Count;
        var byStatus = Enum.GetValues<AppointmentStatus>()
            .Select(status =>
            {
                var count = appointments.Count(a => a.Status == status);
                return new ClinicAppointmentStatusCount
                {
                    Status = status.ToString(),
                    Count = count,
                    PercentageOfTotal = SafePercent(count, total),
                };
            })
            .Where(x => x.Count > 0)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Status, StringComparer.Ordinal)
            .ToList();

        var volumeByDate = BuildVolumeSeries(resolved, appointments);

        var cancelledByClinic = appointments.Count(a => a.Status == AppointmentStatus.CancelledByClinic);
        var cancelledByPatient = appointments.Count(a => a.Status == AppointmentStatus.CancelledByPatient);
        var noShow = appointments.Count(a => a.Status == AppointmentStatus.NoShow);
        var cancelledTotal = cancelledByClinic + cancelledByPatient;

        var response = new ClinicAppointmentReportResponse
        {
            Context = resolved.Context,
            TotalAppointments = total,
            ByStatus = byStatus,
            VolumeByDate = volumeByDate,
            CancellationNoShow = new ClinicCancellationNoShowSummary
            {
                CancelledByClinicCount = cancelledByClinic,
                CancelledByPatientCount = cancelledByPatient,
                NoShowCount = noShow,
                TotalAppointments = total,
                CancellationRate = SafePercent(cancelledTotal, total),
                NoShowRate = SafePercent(noShow, total),
            },
        };

        AuditSucceeded("report_appointments", resolved, "appointments");
        return response;
    }

    public async Task<ClinicDoctorAppointmentsReportResponse> GetDoctorsAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(query, bypass, cancellationToken);
        var appointments = await LoadAppointmentsAsync(resolved, cancellationToken);

        var doctorIds = appointments.Select(a => a.DoctorStaffMemberId).Distinct().ToList();
        var doctorNames = await _dbContext.StaffMembers.AsNoTracking()
            .Where(s => doctorIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayName, s.FirstName, s.LastName })
            .ToListAsync(cancellationToken);

        var nameMap = doctorNames.ToDictionary(
            x => x.Id,
            x =>
            {
                if (!string.IsNullOrWhiteSpace(x.DisplayName))
                {
                    return x.DisplayName.Trim();
                }

                var combined = $"{x.FirstName} {x.LastName}".Trim();
                return string.IsNullOrWhiteSpace(combined) ? "Unknown doctor" : combined;
            });

        var doctors = appointments
            .GroupBy(a => a.DoctorStaffMemberId)
            .Select(g => new ClinicDoctorAppointmentRow
            {
                DoctorStaffMemberId = g.Key,
                DoctorDisplayName = nameMap.TryGetValue(g.Key, out var name) ? name : "Unknown doctor",
                TotalAppointments = g.Count(),
                CompletedCount = g.Count(a => a.Status == AppointmentStatus.Completed),
                CancelledCount = g.Count(a =>
                    a.Status is AppointmentStatus.CancelledByClinic or AppointmentStatus.CancelledByPatient),
                NoShowCount = g.Count(a => a.Status == AppointmentStatus.NoShow),
            })
            .OrderByDescending(x => x.TotalAppointments)
            .ThenBy(x => x.DoctorDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AuditSucceeded("report_doctors", resolved, "doctors");
        return new ClinicDoctorAppointmentsReportResponse
        {
            Context = resolved.Context,
            Doctors = doctors,
        };
    }

    public async Task<ClinicPatientEnrollmentReportResponse> GetPatientsAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(query, bypass, cancellationToken);

        var enrollments = await _dbContext.ClinicPatients.AsNoTracking()
            .Where(cp => cp.ClinicId == resolved.ClinicId)
            .Select(cp => new { cp.Status, cp.RegisteredAtUtc })
            .ToListAsync(cancellationToken);

        var active = enrollments.Count(e => e.Status == ClinicPatientStatus.Active);
        var inactive = enrollments.Count(e => e.Status == ClinicPatientStatus.Inactive);
        var rangeStart = resolved.RangeStartUtc;
        var rangeEnd = resolved.RangeEndUtcExclusive;
        var newInRange = enrollments.Count(e =>
            e.RegisteredAtUtc >= rangeStart && e.RegisteredAtUtc < rangeEnd);

        AuditSucceeded("report_patients", resolved, "patients");
        return new ClinicPatientEnrollmentReportResponse
        {
            Context = resolved.Context,
            ActiveEnrollmentCount = active,
            InactiveEnrollmentCount = inactive,
            TotalClinicPatients = enrollments.Count,
            NewEnrollmentsInRange = newInRange,
        };
    }

    public async Task<ClinicOperationsReportResponse> GetRemindersAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(query, bypass, cancellationToken);

        var reminderCounts = await (
            from reminder in _dbContext.AppointmentReminders.AsNoTracking()
            join appointment in _dbContext.Appointments.AsNoTracking()
                on reminder.AppointmentId equals appointment.Id
            where appointment.ClinicId == resolved.ClinicId
                && reminder.ScheduledAtUtc >= resolved.RangeStartUtc
                && reminder.ScheduledAtUtc < resolved.RangeEndUtcExclusive
            group reminder by reminder.Status
            into g
            select new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(AppointmentReminderStatus status) =>
            reminderCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var failedSummaries = await _dbContext.ClinicAppointmentSummaryRuns.AsNoTracking()
            .CountAsync(
                r => r.ClinicId == resolved.ClinicId
                    && r.Status == ClinicAppointmentSummaryRunStatus.Failed
                    && r.SummaryDate >= resolved.FromDate
                    && r.SummaryDate <= resolved.ToDate,
                cancellationToken);

        var pendingSummaries = await _dbContext.ClinicAppointmentSummaryRuns.AsNoTracking()
            .CountAsync(
                r => r.ClinicId == resolved.ClinicId
                    && r.Status == ClinicAppointmentSummaryRunStatus.Pending
                    && r.SummaryDate >= resolved.FromDate
                    && r.SummaryDate <= resolved.ToDate,
                cancellationToken);

        var activeDoctorCount = await _dbContext.StaffMembers.AsNoTracking()
            .CountAsync(
                s => s.ClinicId == resolved.ClinicId && s.IsActive && s.Role == AppRoles.Doctor,
                cancellationToken);

        var hasAvailability = await _dbContext.DoctorAvailabilities.AsNoTracking()
            .AnyAsync(a => a.ClinicId == resolved.ClinicId && a.IsActive, cancellationToken);

        AuditSucceeded("report_reminders", resolved, "reminders");
        return new ClinicOperationsReportResponse
        {
            Context = resolved.Context,
            PendingReminderCount = Count(AppointmentReminderStatus.Pending),
            ProcessingReminderCount = Count(AppointmentReminderStatus.Processing),
            SentReminderCount = Count(AppointmentReminderStatus.Sent),
            FailedReminderCount = Count(AppointmentReminderStatus.Failed),
            CancelledReminderCount = Count(AppointmentReminderStatus.Cancelled),
            FailedSummaryRunCount = failedSummaries,
            PendingSummaryRunCount = pendingSummaries,
            MissingActiveDoctorAvailability = activeDoctorCount > 0 && !hasAvailability,
        };
    }

    private async Task<ResolvedClinicReport> ResolveAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        EnsureAuthorized();
        var clinicId = await ResolveClinicIdAsync(query, bypass, cancellationToken);

        var clinic = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => new { c.Id, c.Name, c.OrganizationId, c.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ClinicReportException.ClinicNotFound();

        var (fromDate, toDate) = ResolveDateRange(query, clinic.TimeZoneId);
        var rangeStart = _timeZones.ToUtc(fromDate, TimeOnly.MinValue, clinic.TimeZoneId);
        var rangeEnd = _timeZones.ToUtc(toDate.AddDays(1), TimeOnly.MinValue, clinic.TimeZoneId);

        return new ResolvedClinicReport(
            clinic.Id,
            clinic.OrganizationId,
            clinic.TimeZoneId,
            fromDate,
            toDate,
            rangeStart,
            rangeEnd,
            new ClinicReportContext
            {
                ClinicId = clinic.Id,
                ClinicName = clinic.Name,
                OrganizationId = clinic.OrganizationId,
                FromDate = fromDate.ToString("yyyy-MM-dd"),
                ToDate = toDate.ToString("yyyy-MM-dd"),
                TimeZoneId = clinic.TimeZoneId,
                TimeZoneStrategy = "clinic",
            });
    }

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw ClinicReportException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Clinics.ReportsRead);
    }

    private async Task<Guid> ResolveClinicIdAsync(
        ClinicReportQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.ClinicId is null || query.ClinicId == Guid.Empty)
            {
                throw ClinicReportException.ClinicScopeRequired();
            }

            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(c => c.Id == query.ClinicId.Value, cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "clinic_reports",
                    ClinicReportErrorCodes.ClinicNotFound,
                    null,
                    query.ClinicId);
                throw ClinicReportException.ClinicNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("clinic_reports", null, query.ClinicId);
            return query.ClinicId.Value;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (query.ClinicId is Guid requested
            && requested != Guid.Empty
            && requested != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied(
                "clinic_reports_clinic_override",
                ClinicReportErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                requested);
            throw ClinicReportException.InvalidScope();
        }

        if (_currentStaff.ClinicId == Guid.Empty)
        {
            throw ClinicReportException.ClinicNotFound();
        }

        return _currentStaff.ClinicId;
    }

    private (DateOnly From, DateOnly To) ResolveDateRange(ClinicReportQuery query, string timeZoneId)
    {
        var hasFrom = !string.IsNullOrWhiteSpace(query.FromDate);
        var hasTo = !string.IsNullOrWhiteSpace(query.ToDate);
        if (hasFrom != hasTo)
        {
            throw ClinicReportException.InvalidDateRange();
        }

        if (!hasFrom)
        {
            var today = _timeZones.GetClinicDate(_timeProvider.GetUtcNow(), timeZoneId);
            return (today.AddDays(-29), today);
        }

        if (!DateOnly.TryParse(query.FromDate, out var from)
            || !DateOnly.TryParse(query.ToDate, out var to)
            || from > to
            || to.DayNumber - from.DayNumber + 1 > ClinicReportQueryValidator.MaxInclusiveDays)
        {
            throw ClinicReportException.InvalidDateRange();
        }

        return (from, to);
    }

    private async Task<List<AppointmentRow>> LoadAppointmentsAsync(
        ResolvedClinicReport resolved,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == resolved.ClinicId
                && a.AppointmentDateUtc >= resolved.RangeStartUtc
                && a.AppointmentDateUtc < resolved.RangeEndUtcExclusive)
            .Select(a => new AppointmentRow(a.DoctorStaffMemberId, a.Status, a.AppointmentDateUtc))
            .ToListAsync(cancellationToken);
    }

    private IReadOnlyList<ClinicAppointmentVolumeDay> BuildVolumeSeries(
        ResolvedClinicReport resolved,
        IReadOnlyList<AppointmentRow> appointments)
    {
        var grouped = appointments
            .GroupBy(a => _timeZones.GetClinicDate(a.AppointmentDateUtc, resolved.TimeZoneId))
            .ToDictionary(
                g => g.Key,
                g => g.ToList());

        var days = new List<ClinicAppointmentVolumeDay>();
        for (var day = resolved.FromDate; day <= resolved.ToDate; day = day.AddDays(1))
        {
            grouped.TryGetValue(day, out var rows);
            rows ??= [];
            days.Add(new ClinicAppointmentVolumeDay
            {
                LocalDate = day.ToString("yyyy-MM-dd"),
                AppointmentCount = rows.Count,
                CompletedCount = rows.Count(a => a.Status == AppointmentStatus.Completed),
                CancelledCount = rows.Count(a =>
                    a.Status is AppointmentStatus.CancelledByClinic or AppointmentStatus.CancelledByPatient),
                NoShowCount = rows.Count(a => a.Status == AppointmentStatus.NoShow),
            });
        }

        return days;
    }

    private void AuditSucceeded(string operation, ResolvedClinicReport resolved, string reportType)
    {
        _logger.LogInformation(
            "Clinic report loaded. Operation={Operation} ClinicId={ClinicId} From={From} To={To}",
            operation,
            resolved.ClinicId,
            resolved.FromDate,
            resolved.ToDate);

        _audit.ReportOperation(
            operation,
            "succeeded",
            resolved.OrganizationId,
            resolved.ClinicId,
            reportType);
    }

    private static decimal SafePercent(int part, int total) =>
        total <= 0 ? 0m : Math.Round(part * 100m / total, 2, MidpointRounding.AwayFromZero);

    private sealed record AppointmentRow(
        Guid DoctorStaffMemberId,
        AppointmentStatus Status,
        DateTimeOffset AppointmentDateUtc);

    private sealed record ResolvedClinicReport(
        Guid ClinicId,
        Guid OrganizationId,
        string TimeZoneId,
        DateOnly FromDate,
        DateOnly ToDate,
        DateTimeOffset RangeStartUtc,
        DateTimeOffset RangeEndUtcExclusive,
        ClinicReportContext Context);
}
