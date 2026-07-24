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
/// Clinic-scoped operational aggregates for the Clinic Admin dashboard.
/// Date boundaries use the clinic IANA timezone (not server-local time).
/// </summary>
public sealed class ClinicDashboardService : IClinicDashboardService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicDashboardService> _logger;

    public ClinicDashboardService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<ClinicDashboardService> logger)
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

    public async Task<ClinicDashboardResponse> GetAsync(
        ClinicDashboardQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);

        DateOnly? explicitDate = null;
        if (!string.IsNullOrWhiteSpace(query.Date))
        {
            if (!DateOnly.TryParse(query.Date, out var parsed))
            {
                throw ClinicDashboardException.InvalidDate();
            }

            explicitDate = parsed;
        }

        var clinic = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == scope.ClinicId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.OrganizationId,
                c.TimeZoneId,
                c.IsActive,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ClinicDashboardException.ClinicNotFound();

        var organizationName = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == clinic.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        var nowUtc = _timeProvider.GetUtcNow();
        var dashboardDate = explicitDate ?? _timeZones.GetClinicDate(nowUtc, clinic.TimeZoneId);
        var dayStart = _timeZones.ToUtc(dashboardDate, TimeOnly.MinValue, clinic.TimeZoneId);
        var dayEnd = _timeZones.ToUtc(dashboardDate.AddDays(1), TimeOnly.MinValue, clinic.TimeZoneId);
        var monthStartDate = new DateOnly(dashboardDate.Year, dashboardDate.Month, 1);
        var monthEndDate = monthStartDate.AddMonths(1);
        var monthStart = _timeZones.ToUtc(monthStartDate, TimeOnly.MinValue, clinic.TimeZoneId);
        var monthEnd = _timeZones.ToUtc(monthEndDate, TimeOnly.MinValue, clinic.TimeZoneId);

        var activeStaffCount = await _dbContext.StaffMembers.AsNoTracking()
            .CountAsync(
                s => s.ClinicId == clinic.Id && s.IsActive,
                cancellationToken);

        var activeDoctorCount = await _dbContext.StaffMembers.AsNoTracking()
            .CountAsync(
                s => s.ClinicId == clinic.Id && s.IsActive && s.Role == AppRoles.Doctor,
                cancellationToken);

        var activePatientCount = await _dbContext.ClinicPatients.AsNoTracking()
            .Where(cp => cp.ClinicId == clinic.Id && cp.Status == ClinicPatientStatus.Active)
            .Select(cp => cp.PatientId)
            .Distinct()
            .CountAsync(cancellationToken);

        var todayByStatus = await _dbContext.Appointments.AsNoTracking()
            .Where(a => a.ClinicId == clinic.Id
                && a.AppointmentDateUtc >= dayStart
                && a.AppointmentDateUtc < dayEnd)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var statusTotals = Enum.GetValues<AppointmentStatus>()
            .ToDictionary(s => s, _ => 0);
        foreach (var row in todayByStatus)
        {
            statusTotals[row.Status] = row.Count;
        }

        var cancelled = statusTotals.GetValueOrDefault(AppointmentStatus.CancelledByPatient)
            + statusTotals.GetValueOrDefault(AppointmentStatus.CancelledByClinic);
        var todayTotal = statusTotals.Values.Sum();

        var monthlyAppointmentCount = await _dbContext.Appointments.AsNoTracking()
            .CountAsync(
                a => a.ClinicId == clinic.Id
                    && a.AppointmentDateUtc >= monthStart
                    && a.AppointmentDateUtc < monthEnd,
                cancellationToken);

        var failedReminders = await (
            from reminder in _dbContext.AppointmentReminders.AsNoTracking()
            join appointment in _dbContext.Appointments.AsNoTracking()
                on reminder.AppointmentId equals appointment.Id
            where reminder.Status == AppointmentReminderStatus.Failed
                && appointment.ClinicId == clinic.Id
            select reminder.Id).CountAsync(cancellationToken);

        var failedSummaries = await _dbContext.ClinicAppointmentSummaryRuns.AsNoTracking()
            .CountAsync(
                r => r.ClinicId == clinic.Id
                    && r.Status == ClinicAppointmentSummaryRunStatus.Failed,
                cancellationToken);

        var hasAvailability = await _dbContext.DoctorAvailabilities.AsNoTracking()
            .AnyAsync(
                a => a.ClinicId == clinic.Id && a.IsActive,
                cancellationToken);

        _logger.LogInformation(
            "Clinic dashboard loaded. ActorUserId={ActorUserId} ClinicId={ClinicId} OrganizationId={OrganizationId}",
            _currentUser.UserId,
            clinic.Id,
            clinic.OrganizationId);

        return new ClinicDashboardResponse
        {
            ClinicId = clinic.Id,
            ClinicName = clinic.Name,
            OrganizationId = clinic.OrganizationId,
            OrganizationName = organizationName,
            DefaultTimeZoneId = clinic.TimeZoneId,
            DashboardDate = dashboardDate.ToString("yyyy-MM-dd"),
            TimeZoneStrategy = "clinic",
            ActiveStaffCount = activeStaffCount,
            ActiveDoctorCount = activeDoctorCount,
            ActivePatientCount = activePatientCount,
            TodayAppointmentCount = todayTotal,
            TodayAppointmentsByStatus = new ClinicDashboardAppointmentByStatus
            {
                RequestedCount = statusTotals.GetValueOrDefault(AppointmentStatus.Requested),
                ConfirmedCount = statusTotals.GetValueOrDefault(AppointmentStatus.Confirmed),
                CheckedInCount = statusTotals.GetValueOrDefault(AppointmentStatus.CheckedIn),
                InProgressCount = statusTotals.GetValueOrDefault(AppointmentStatus.InProgress),
                CompletedCount = statusTotals.GetValueOrDefault(AppointmentStatus.Completed),
                CancelledCount = cancelled,
                NoShowCount = statusTotals.GetValueOrDefault(AppointmentStatus.NoShow),
            },
            MonthlyAppointmentCount = monthlyAppointmentCount,
            FailedReminderCount = failedReminders,
            OperationalWarnings = new ClinicDashboardOperationalWarnings
            {
                FailedReminderCount = failedReminders,
                FailedClinicSummaryCount = failedSummaries,
                MissingActiveDoctor = activeDoctorCount == 0,
                MissingAvailability = activeDoctorCount > 0 && !hasAvailability,
            },
        };
    }

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw ClinicDashboardException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Clinics.DashboardRead);
    }

    private async Task<ClinicScope> ResolveScopeAsync(
        ClinicDashboardQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.ClinicId is null || query.ClinicId == Guid.Empty)
            {
                throw ClinicDashboardException.ClinicScopeRequired();
            }

            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(c => c.Id == query.ClinicId.Value, cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "clinic_dashboard",
                    ClinicDashboardErrorCodes.ClinicNotFound,
                    null,
                    query.ClinicId);
                throw ClinicDashboardException.ClinicNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("clinic_dashboard", null, query.ClinicId);
            return new ClinicScope(query.ClinicId.Value);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        // CLINIC_ADMIN (and any non-PA holder of the permission): membership clinic only.
        // Never trust a client ClinicId that differs from membership.
        if (query.ClinicId is Guid requested
            && requested != Guid.Empty
            && requested != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied(
                "clinic_dashboard_clinic_override",
                ClinicDashboardErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                requested);
            throw ClinicDashboardException.InvalidScope();
        }

        if (_currentStaff.ClinicId == Guid.Empty)
        {
            throw ClinicDashboardException.ClinicNotFound();
        }

        return new ClinicScope(_currentStaff.ClinicId);
    }

    private sealed record ClinicScope(Guid ClinicId);
}
