using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Organizations;

/// <summary>
/// Organization-scoped operational reports for Organization Admins.
/// Appointment date windows use per-clinic local calendars (Option A) when aggregating.
/// </summary>
public sealed class OrganizationReportService : IOrganizationReportService
{
    public const int MaxFailureDetailRows = 200;

    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrganizationReportService> _logger;

    public OrganizationReportService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<OrganizationReportService> logger)
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

    public async Task<OrganizationAppointmentReportResponse> GetAppointmentsAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await BeginAsync(query, bypass, "appointments", cancellationToken);
        var (fromDate, toDate) = ResolveDateRange(query, scope);
        var appointments = await LoadAppointmentsInRangeAsync(scope, fromDate, toDate, cancellationToken);

        var byStatus = appointments
            .GroupBy(a => a.Status)
            .Select(g => new OrganizationReportStatusCount { Status = g.Key.ToString(), Count = g.Count() })
            .OrderBy(x => x.Status, StringComparer.Ordinal)
            .ToList();

        var clinicNames = scope.Clinics.ToDictionary(c => c.Id, c => c.Name);
        var byClinic = appointments
            .GroupBy(a => a.ClinicId)
            .Select(g => new OrganizationReportClinicAppointmentCount
            {
                ClinicId = g.Key,
                ClinicName = clinicNames.GetValueOrDefault(g.Key, g.Key.ToString("N")),
                TotalAppointments = g.Count(),
                CancellationCount = g.Count(a =>
                    a.Status is AppointmentStatus.CancelledByPatient or AppointmentStatus.CancelledByClinic),
                NoShowCount = g.Count(a => a.Status == AppointmentStatus.NoShow),
            })
            .OrderBy(x => x.ClinicName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var doctorIds = appointments
            .Select(a => a.DoctorStaffMemberId)
            .Distinct()
            .ToList();
        var doctorNames = doctorIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _dbContext.StaffMembers.AsNoTracking()
                .Where(s => doctorIds.Contains(s.Id))
                .Select(s => new { s.Id, s.DisplayName, s.FirstName, s.LastName })
                .ToListAsync(cancellationToken))
            .ToDictionary(
                x => x.Id,
                x =>
                {
                    if (!string.IsNullOrWhiteSpace(x.DisplayName))
                    {
                        return x.DisplayName!;
                    }

                    var combined = $"{x.FirstName} {x.LastName}".Trim();
                    return string.IsNullOrWhiteSpace(combined) ? x.Id.ToString("N") : combined;
                });

        var byDoctor = appointments
            .GroupBy(a => new { a.DoctorStaffMemberId, a.ClinicId })
            .Select(g => new OrganizationReportDoctorAppointmentCount
            {
                DoctorStaffMemberId = g.Key.DoctorStaffMemberId,
                DoctorDisplayName = doctorNames.TryGetValue(g.Key.DoctorStaffMemberId, out var name)
                    ? name
                    : g.Key.DoctorStaffMemberId.ToString("N"),
                ClinicId = g.Key.ClinicId,
                ClinicName = clinicNames.GetValueOrDefault(g.Key.ClinicId, g.Key.ClinicId.ToString("N")),
                Count = g.Count(),
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.DoctorDisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var totals = new OrganizationAppointmentReportTotals
        {
            TotalAppointments = appointments.Count,
            CancellationCount = appointments.Count(a =>
                a.Status is AppointmentStatus.CancelledByPatient or AppointmentStatus.CancelledByClinic),
            NoShowCount = appointments.Count(a => a.Status == AppointmentStatus.NoShow),
            CompletedCount = appointments.Count(a => a.Status == AppointmentStatus.Completed),
            ConfirmedCount = appointments.Count(a => a.Status == AppointmentStatus.Confirmed),
            RequestedCount = appointments.Count(a => a.Status == AppointmentStatus.Requested),
        };

        Complete("appointments", scope);
        return new OrganizationAppointmentReportResponse
        {
            Context = BuildContext(scope, fromDate, toDate),
            Totals = totals,
            ByClinic = byClinic,
            ByStatus = byStatus,
            ByDoctor = byDoctor,
        };
    }

    public async Task<OrganizationStaffReportResponse> GetStaffAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await BeginAsync(query, bypass, "staff", cancellationToken);
        var clinicIds = scope.Clinics.Select(c => c.Id).ToList();

        var rows = clinicIds.Count == 0
            ? []
            : await _dbContext.StaffMembers.AsNoTracking()
                .Where(s => s.OrganizationId == scope.OrganizationId && clinicIds.Contains(s.ClinicId))
                .Select(s => new { s.ClinicId, s.IsActive, s.Role })
                .ToListAsync(cancellationToken);

        var byClinic = scope.Clinics
            .Select(clinic =>
            {
                var clinicRows = rows.Where(r => r.ClinicId == clinic.Id).ToList();
                int RoleCount(string role) =>
                    clinicRows.Count(r => r.IsActive && string.Equals(r.Role, role, StringComparison.Ordinal));

                return new OrganizationReportStaffClinicCount
                {
                    ClinicId = clinic.Id,
                    ClinicName = clinic.Name,
                    ActiveStaffCount = clinicRows.Count(r => r.IsActive),
                    InactiveStaffCount = clinicRows.Count(r => !r.IsActive),
                    DoctorCount = RoleCount(AppRoles.Doctor),
                    NurseCount = RoleCount(AppRoles.Nurse),
                    ReceptionistCount = RoleCount(AppRoles.Receptionist),
                    ClinicAdminCount = RoleCount(AppRoles.ClinicAdmin),
                    OrganizationAdminCount = RoleCount(AppRoles.OrganizationAdmin),
                };
            })
            .OrderBy(x => x.ClinicName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Complete("staff", scope);
        return new OrganizationStaffReportResponse
        {
            Context = BuildContext(scope, fromDate: null, toDate: null),
            TotalActiveStaff = byClinic.Sum(x => x.ActiveStaffCount),
            TotalInactiveStaff = byClinic.Sum(x => x.InactiveStaffCount),
            ByClinic = byClinic,
        };
    }

    public async Task<OrganizationPatientReportResponse> GetPatientsAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await BeginAsync(query, bypass, "patients", cancellationToken);
        var clinicIds = scope.Clinics.Select(c => c.Id).ToList();

        var enrollments = clinicIds.Count == 0
            ? []
            : await _dbContext.ClinicPatients.AsNoTracking()
                .Where(cp => clinicIds.Contains(cp.ClinicId))
                .Select(cp => new { cp.ClinicId, cp.PatientId, cp.Status })
                .ToListAsync(cancellationToken);

        var byClinic = scope.Clinics
            .Select(clinic =>
            {
                var clinicRows = enrollments.Where(e => e.ClinicId == clinic.Id).ToList();
                return new OrganizationReportPatientClinicCount
                {
                    ClinicId = clinic.Id,
                    ClinicName = clinic.Name,
                    ActiveEnrollmentCount = clinicRows.Count(e => e.Status == ClinicPatientStatus.Active),
                    InactiveEnrollmentCount = clinicRows.Count(e => e.Status != ClinicPatientStatus.Active),
                };
            })
            .OrderBy(x => x.ClinicName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Complete("patients", scope);
        return new OrganizationPatientReportResponse
        {
            Context = BuildContext(scope, fromDate: null, toDate: null),
            TotalActiveEnrollments = byClinic.Sum(x => x.ActiveEnrollmentCount),
            TotalInactiveEnrollments = byClinic.Sum(x => x.InactiveEnrollmentCount),
            DistinctPatientCount = enrollments.Select(e => e.PatientId).Distinct().Count(),
            ByClinic = byClinic,
        };
    }

    public async Task<OrganizationAvailabilityReportResponse> GetAvailabilityAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await BeginAsync(query, bypass, "availability", cancellationToken);
        var clinicIds = scope.Clinics.Select(c => c.Id).ToList();

        var doctors = clinicIds.Count == 0
            ? []
            : await _dbContext.StaffMembers.AsNoTracking()
                .Where(s => s.OrganizationId == scope.OrganizationId
                    && clinicIds.Contains(s.ClinicId)
                    && s.IsActive
                    && s.Role == AppRoles.Doctor)
                .Select(s => new { s.Id, s.ClinicId })
                .ToListAsync(cancellationToken);

        var windows = clinicIds.Count == 0
            ? []
            : await _dbContext.DoctorAvailabilities.AsNoTracking()
                .Where(a => a.OrganizationId == scope.OrganizationId
                    && clinicIds.Contains(a.ClinicId)
                    && a.IsActive)
                .Select(a => new { a.ClinicId, a.DoctorStaffMemberId })
                .ToListAsync(cancellationToken);

        var exceptions = clinicIds.Count == 0
            ? []
            : await _dbContext.DoctorAvailabilityExceptions.AsNoTracking()
                .Where(e => e.OrganizationId == scope.OrganizationId && clinicIds.Contains(e.ClinicId))
                .GroupBy(e => e.ClinicId)
                .Select(g => new { ClinicId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

        var exceptionLookup = exceptions.ToDictionary(x => x.ClinicId, x => x.Count);

        var byClinic = scope.Clinics
            .Select(clinic =>
            {
                var clinicDoctors = doctors.Where(d => d.ClinicId == clinic.Id).ToList();
                var doctorIds = clinicDoctors.Select(d => d.Id).ToHashSet();
                var clinicWindows = windows.Where(w => w.ClinicId == clinic.Id).ToList();
                var doctorsWithAvailability = clinicWindows
                    .Select(w => w.DoctorStaffMemberId)
                    .Where(doctorIds.Contains)
                    .Distinct()
                    .Count();
                var activeDoctorCount = clinicDoctors.Count;
                var hasGap = activeDoctorCount > 0 && doctorsWithAvailability < activeDoctorCount;

                return new OrganizationReportAvailabilityClinicCoverage
                {
                    ClinicId = clinic.Id,
                    ClinicName = clinic.Name,
                    ClinicIsActive = clinic.IsActive,
                    ActiveDoctorCount = activeDoctorCount,
                    DoctorsWithActiveAvailability = doctorsWithAvailability,
                    ActiveAvailabilityWindowCount = clinicWindows.Count,
                    AvailabilityExceptionCount = exceptionLookup.GetValueOrDefault(clinic.Id),
                    HasCoverageGap = hasGap || (clinic.IsActive && activeDoctorCount == 0),
                };
            })
            .OrderBy(x => x.ClinicName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Complete("availability", scope);
        return new OrganizationAvailabilityReportResponse
        {
            Context = BuildContext(scope, fromDate: null, toDate: null),
            ByClinic = byClinic,
        };
    }

    public async Task<OrganizationReminderFailureReportResponse> GetReminderFailuresAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await BeginAsync(query, bypass, "reminder-failures", cancellationToken);
        var (fromDate, toDate) = ResolveDateRange(query, scope);
        var clinicNames = scope.Clinics.ToDictionary(c => c.Id, c => c.Name);

        var ranges = BuildUtcRanges(scope.Clinics, fromDate, toDate);
        var failed = new List<(Guid ReminderId, Guid AppointmentId, Guid ClinicId, AppointmentReminderType Type,
            DateTimeOffset ScheduledAtUtc, int AttemptCount, string? LastError, string? BackgroundJobId)>();

        foreach (var range in ranges)
        {
            var rows = await (
                from reminder in _dbContext.AppointmentReminders.AsNoTracking()
                join appointment in _dbContext.Appointments.AsNoTracking()
                    on reminder.AppointmentId equals appointment.Id
                where reminder.Status == AppointmentReminderStatus.Failed
                    && appointment.OrganizationId == scope.OrganizationId
                    && range.ClinicIds.Contains(appointment.ClinicId)
                    && reminder.ScheduledAtUtc >= range.Start
                    && reminder.ScheduledAtUtc < range.End
                select new
                {
                    reminder.Id,
                    reminder.AppointmentId,
                    appointment.ClinicId,
                    reminder.ReminderType,
                    reminder.ScheduledAtUtc,
                    reminder.AttemptCount,
                    reminder.LastError,
                    reminder.BackgroundJobId,
                }).ToListAsync(cancellationToken);

            failed.AddRange(rows.Select(r => (
                ReminderId: r.Id,
                AppointmentId: r.AppointmentId,
                ClinicId: r.ClinicId,
                Type: r.ReminderType,
                ScheduledAtUtc: r.ScheduledAtUtc,
                AttemptCount: r.AttemptCount,
                LastError: (string?)r.LastError,
                BackgroundJobId: (string?)r.BackgroundJobId)));
        }

        var byClinic = failed
            .GroupBy(x => x.ClinicId)
            .Select(g => new OrganizationReportClinicFailureCount
            {
                ClinicId = g.Key,
                ClinicName = clinicNames.GetValueOrDefault(g.Key, g.Key.ToString("N")),
                FailedCount = g.Count(),
            })
            .OrderByDescending(x => x.FailedCount)
            .ThenBy(x => x.ClinicName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = failed
            .OrderByDescending(x => x.ScheduledAtUtc)
            .ThenBy(x => x.ReminderId)
            .Take(MaxFailureDetailRows)
            .Select(x => new OrganizationReportReminderFailureItem
            {
                ReminderId = x.ReminderId,
                AppointmentId = x.AppointmentId,
                ClinicId = x.ClinicId,
                ReminderType = x.Type.ToString(),
                ScheduledAtUtc = x.ScheduledAtUtc,
                AttemptCount = x.AttemptCount,
                ErrorCode = string.IsNullOrWhiteSpace(x.LastError)
                    ? null
                    : AppointmentReminderErrorCodes.ReminderDeliveryFailed,
                BackgroundJobId = x.BackgroundJobId,
            })
            .ToList();

        Complete("reminder-failures", scope);
        return new OrganizationReminderFailureReportResponse
        {
            Context = BuildContext(scope, fromDate, toDate),
            FailedCount = failed.Count,
            ByClinic = byClinic,
            Items = items,
        };
    }

    public async Task<OrganizationSummaryFailureReportResponse> GetSummaryFailuresAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await BeginAsync(query, bypass, "summary-failures", cancellationToken);
        var (fromDate, toDate) = ResolveDateRange(query, scope);
        var clinicIds = scope.Clinics.Select(c => c.Id).ToList();
        var clinicNames = scope.Clinics.ToDictionary(c => c.Id, c => c.Name);

        var runs = clinicIds.Count == 0
            ? []
            : await _dbContext.ClinicAppointmentSummaryRuns.AsNoTracking()
                .Where(r => r.OrganizationId == scope.OrganizationId
                    && clinicIds.Contains(r.ClinicId)
                    && r.Status == ClinicAppointmentSummaryRunStatus.Failed
                    && r.SummaryDate >= fromDate
                    && r.SummaryDate <= toDate)
                .OrderByDescending(r => r.SummaryDate)
                .ThenByDescending(r => r.ScheduledAtUtc)
                .ToListAsync(cancellationToken);

        var byClinic = runs
            .GroupBy(r => r.ClinicId)
            .Select(g => new OrganizationReportClinicFailureCount
            {
                ClinicId = g.Key,
                ClinicName = clinicNames.GetValueOrDefault(g.Key, g.Key.ToString("N")),
                FailedCount = g.Count(),
            })
            .OrderByDescending(x => x.FailedCount)
            .ThenBy(x => x.ClinicName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = runs
            .Take(MaxFailureDetailRows)
            .Select(r => new OrganizationReportSummaryFailureItem
            {
                RunId = r.Id,
                ClinicId = r.ClinicId,
                SummaryDate = r.SummaryDate.ToString("yyyy-MM-dd"),
                ScheduledAtUtc = r.ScheduledAtUtc,
                AttemptCount = r.AttemptCount,
                LastErrorCode = r.LastErrorCode,
                BackgroundJobId = r.BackgroundJobId,
            })
            .ToList();

        Complete("summary-failures", scope);
        return new OrganizationSummaryFailureReportResponse
        {
            Context = BuildContext(scope, fromDate, toDate),
            FailedCount = runs.Count,
            ByClinic = byClinic,
            Items = items,
        };
    }

    public async Task<OrganizationReportCsvResult> ExportCsvAsync(
        string reportType,
        OrganizationReportQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var key = (reportType ?? string.Empty).Trim().ToLowerInvariant();
        byte[] content;
        string fileName;

        switch (key)
        {
            case OrganizationReportTypes.Appointments:
            {
                var report = await GetAppointmentsAsync(query, bypass, cancellationToken);
                content = OrganizationReportCsvWriter.Write(
                    ["ClinicId", "ClinicName", "TotalAppointments", "CancellationCount", "NoShowCount"],
                    report.ByClinic.Select(r => (IReadOnlyList<string?>)
                    [
                        r.ClinicId.ToString("D"),
                        r.ClinicName,
                        r.TotalAppointments.ToString(),
                        r.CancellationCount.ToString(),
                        r.NoShowCount.ToString(),
                    ]));
                fileName = $"organization-appointments-{DateStamp()}.csv";
                break;
            }
            case OrganizationReportTypes.Staff:
            {
                var report = await GetStaffAsync(query, bypass, cancellationToken);
                content = OrganizationReportCsvWriter.Write(
                    [
                        "ClinicId", "ClinicName", "ActiveStaffCount", "InactiveStaffCount", "DoctorCount",
                        "NurseCount", "ReceptionistCount", "ClinicAdminCount", "OrganizationAdminCount",
                    ],
                    report.ByClinic.Select(r => (IReadOnlyList<string?>)
                    [
                        r.ClinicId.ToString("D"),
                        r.ClinicName,
                        r.ActiveStaffCount.ToString(),
                        r.InactiveStaffCount.ToString(),
                        r.DoctorCount.ToString(),
                        r.NurseCount.ToString(),
                        r.ReceptionistCount.ToString(),
                        r.ClinicAdminCount.ToString(),
                        r.OrganizationAdminCount.ToString(),
                    ]));
                fileName = $"organization-staff-{DateStamp()}.csv";
                break;
            }
            case OrganizationReportTypes.Patients:
            {
                var report = await GetPatientsAsync(query, bypass, cancellationToken);
                content = OrganizationReportCsvWriter.Write(
                    ["ClinicId", "ClinicName", "ActiveEnrollmentCount", "InactiveEnrollmentCount"],
                    report.ByClinic.Select(r => (IReadOnlyList<string?>)
                    [
                        r.ClinicId.ToString("D"),
                        r.ClinicName,
                        r.ActiveEnrollmentCount.ToString(),
                        r.InactiveEnrollmentCount.ToString(),
                    ]));
                fileName = $"organization-patients-{DateStamp()}.csv";
                break;
            }
            case OrganizationReportTypes.Availability:
            {
                var report = await GetAvailabilityAsync(query, bypass, cancellationToken);
                content = OrganizationReportCsvWriter.Write(
                    [
                        "ClinicId", "ClinicName", "ClinicIsActive", "ActiveDoctorCount",
                        "DoctorsWithActiveAvailability", "ActiveAvailabilityWindowCount",
                        "AvailabilityExceptionCount", "HasCoverageGap",
                    ],
                    report.ByClinic.Select(r => (IReadOnlyList<string?>)
                    [
                        r.ClinicId.ToString("D"),
                        r.ClinicName,
                        r.ClinicIsActive ? "true" : "false",
                        r.ActiveDoctorCount.ToString(),
                        r.DoctorsWithActiveAvailability.ToString(),
                        r.ActiveAvailabilityWindowCount.ToString(),
                        r.AvailabilityExceptionCount.ToString(),
                        r.HasCoverageGap ? "true" : "false",
                    ]));
                fileName = $"organization-availability-{DateStamp()}.csv";
                break;
            }
            case OrganizationReportTypes.ReminderFailures:
            {
                var report = await GetReminderFailuresAsync(query, bypass, cancellationToken);
                content = OrganizationReportCsvWriter.Write(
                    [
                        "ReminderId", "AppointmentId", "ClinicId", "ReminderType", "ScheduledAtUtc",
                        "AttemptCount", "ErrorCode", "BackgroundJobId",
                    ],
                    report.Items.Select(r => (IReadOnlyList<string?>)
                    [
                        r.ReminderId.ToString("D"),
                        r.AppointmentId.ToString("D"),
                        r.ClinicId.ToString("D"),
                        r.ReminderType,
                        OrganizationReportCsvWriter.FormatUtc(r.ScheduledAtUtc),
                        r.AttemptCount.ToString(),
                        r.ErrorCode,
                        r.BackgroundJobId,
                    ]));
                fileName = $"organization-reminder-failures-{DateStamp()}.csv";
                break;
            }
            case OrganizationReportTypes.SummaryFailures:
            {
                var report = await GetSummaryFailuresAsync(query, bypass, cancellationToken);
                content = OrganizationReportCsvWriter.Write(
                    [
                        "RunId", "ClinicId", "SummaryDate", "ScheduledAtUtc", "AttemptCount", "LastErrorCode",
                        "BackgroundJobId",
                    ],
                    report.Items.Select(r => (IReadOnlyList<string?>)
                    [
                        r.RunId.ToString("D"),
                        r.ClinicId.ToString("D"),
                        r.SummaryDate,
                        OrganizationReportCsvWriter.FormatUtc(r.ScheduledAtUtc),
                        r.AttemptCount.ToString(),
                        r.LastErrorCode,
                        r.BackgroundJobId,
                    ]));
                fileName = $"organization-summary-failures-{DateStamp()}.csv";
                break;
            }
            default:
                throw OrganizationReportException.UnknownReport();
        }

        _audit.ReportOperation(
            "report_export_csv",
            "succeeded",
            null,
            null,
            key);
        return new OrganizationReportCsvResult
        {
            FileName = fileName,
            ContentType = "text/csv; charset=utf-8",
            Content = content,
        };
    }

    private async Task<ReportScope> BeginAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass,
        string reportName,
        CancellationToken cancellationToken)
    {
        EnsureAuthorized();
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        _logger.LogInformation(
            "Organization report requested. ActorUserId={ActorUserId} Report={Report} OrganizationId={OrganizationId} ClinicId={ClinicId}",
            _currentUser.UserId,
            reportName,
            scope.OrganizationId,
            scope.ClinicId);
        return scope;
    }

    private void Complete(string reportName, ReportScope scope) =>
        _audit.ReportOperation(
            "report_" + reportName.Replace('-', '_'),
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId,
            reportName);

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw OrganizationReportException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Organizations.ReportsRead);
    }

    private async Task<ReportScope> ResolveScopeAsync(
        OrganizationReportQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.OrganizationId is null || query.OrganizationId == Guid.Empty)
            {
                throw OrganizationReportException.OrganizationScopeRequired();
            }

            var org = await _dbContext.Organizations.AsNoTracking()
                .Where(o => o.Id == query.OrganizationId.Value)
                .Select(o => new { o.Id, o.Name })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw OrganizationReportException.OrganizationNotFound();

            var clinics = await LoadClinicsAsync(org.Id, query.ClinicId, cancellationToken);
            _audit.ExplicitPlatformBypassUsed("organization_reports", org.Id, query.ClinicId);
            return new ReportScope(org.Id, org.Name, query.ClinicId, clinics);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw OrganizationReportException.AccessDenied();
        }

        if (query.OrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "organization_reports_org_override",
                OrganizationReportErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw OrganizationReportException.InvalidScope();
        }

        var organizationName = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == _currentStaff.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw OrganizationReportException.OrganizationNotFound();

        var scopedClinics = await LoadClinicsAsync(_currentStaff.OrganizationId, query.ClinicId, cancellationToken);
        return new ReportScope(_currentStaff.OrganizationId, organizationName, query.ClinicId, scopedClinics);
    }

    private async Task<IReadOnlyList<ClinicRow>> LoadClinicsAsync(
        Guid organizationId,
        Guid? clinicIdFilter,
        CancellationToken cancellationToken)
    {
        if (clinicIdFilter is Guid requiredClinic && requiredClinic != Guid.Empty)
        {
            var clinic = await _dbContext.Clinics.AsNoTracking()
                .Where(c => c.Id == requiredClinic && c.OrganizationId == organizationId)
                .Select(c => new ClinicRow(c.Id, c.Name, c.IsActive, c.TimeZoneId))
                .SingleOrDefaultAsync(cancellationToken);

            if (clinic is null)
            {
                _audit.CrossTenantDenied(
                    "organization_reports_clinic",
                    OrganizationReportErrorCodes.ClinicNotFound,
                    organizationId,
                    requiredClinic);
                throw OrganizationReportException.ClinicNotFound();
            }

            return [clinic];
        }

        return await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .Select(c => new ClinicRow(c.Id, c.Name, c.IsActive, c.TimeZoneId))
            .ToListAsync(cancellationToken);
    }

    private (DateOnly From, DateOnly To) ResolveDateRange(OrganizationReportQuery query, ReportScope scope)
    {
        if (!string.IsNullOrWhiteSpace(query.FromDate) && !string.IsNullOrWhiteSpace(query.ToDate))
        {
            if (!DateOnly.TryParse(query.FromDate, out var from) || !DateOnly.TryParse(query.ToDate, out var to))
            {
                throw OrganizationReportException.InvalidDateRange();
            }

            if (from > to || to.DayNumber - from.DayNumber + 1 > OrganizationReportQueryValidator.MaxInclusiveDays)
            {
                throw OrganizationReportException.InvalidDateRange();
            }

            return (from, to);
        }

        var now = _timeProvider.GetUtcNow();
        if (scope.Clinics.Count == 1)
        {
            var today = _timeZones.GetClinicDate(now, scope.Clinics[0].TimeZoneId);
            return (today, today);
        }

        // Multi-clinic default: use UTC calendar date as a stable shared window label.
        var utcToday = DateOnly.FromDateTime(now.UtcDateTime);
        return (utcToday, utcToday);
    }

    private async Task<List<AppointmentRow>> LoadAppointmentsInRangeAsync(
        ReportScope scope,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var ranges = BuildUtcRanges(scope.Clinics, fromDate, toDate);
        var results = new List<AppointmentRow>();
        foreach (var range in ranges)
        {
            var rows = await _dbContext.Appointments.AsNoTracking()
                .Where(a => a.OrganizationId == scope.OrganizationId
                    && range.ClinicIds.Contains(a.ClinicId)
                    && a.AppointmentDateUtc >= range.Start
                    && a.AppointmentDateUtc < range.End)
                .Select(a => new AppointmentRow(a.Id, a.ClinicId, a.DoctorStaffMemberId, a.Status, a.AppointmentDateUtc))
                .ToListAsync(cancellationToken);
            results.AddRange(rows);
        }

        return results;
    }

    private List<UtcRange> BuildUtcRanges(IReadOnlyList<ClinicRow> clinics, DateOnly fromDate, DateOnly toDate)
    {
        // Expand inclusive local dates to half-open UTC windows per timezone group.
        return clinics
            .GroupBy(c => c.TimeZoneId, StringComparer.Ordinal)
            .Select(g =>
            {
                var start = _timeZones.ToUtc(fromDate, TimeOnly.MinValue, g.Key);
                var end = _timeZones.ToUtc(toDate.AddDays(1), TimeOnly.MinValue, g.Key);
                return new UtcRange(g.Select(c => c.Id).ToList(), start, end);
            })
            .ToList();
    }

    private static OrganizationReportContext BuildContext(ReportScope scope, DateOnly? fromDate, DateOnly? toDate)
    {
        ClinicRow? selected = null;
        if (scope.ClinicId is Guid id)
        {
            selected = scope.Clinics.SingleOrDefault(c => c.Id == id);
        }

        return new OrganizationReportContext
        {
            OrganizationId = scope.OrganizationId,
            OrganizationName = scope.OrganizationName,
            SelectedClinicId = selected?.Id,
            SelectedClinicName = selected?.Name,
            FromDate = fromDate?.ToString("yyyy-MM-dd"),
            ToDate = toDate?.ToString("yyyy-MM-dd"),
            TimeZoneId = selected?.TimeZoneId,
            TimeZoneStrategy = selected is null ? "per_clinic_local" : "clinic",
        };
    }

    private string DateStamp() =>
        _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record ClinicRow(Guid Id, string Name, bool IsActive, string TimeZoneId);

    private sealed record ReportScope(
        Guid OrganizationId,
        string OrganizationName,
        Guid? ClinicId,
        IReadOnlyList<ClinicRow> Clinics);

    private sealed record AppointmentRow(
        Guid Id,
        Guid ClinicId,
        Guid DoctorStaffMemberId,
        AppointmentStatus Status,
        DateTimeOffset AppointmentDateUtc);

    private sealed record UtcRange(IReadOnlyList<Guid> ClinicIds, DateTimeOffset Start, DateTimeOffset End);
}
