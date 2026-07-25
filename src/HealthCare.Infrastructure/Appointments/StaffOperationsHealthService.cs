using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Configuration;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Appointments;

public sealed class StaffOperationsHealthService : IStaffOperationsHealthService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IAppointmentReminderSender _reminderSender;
    private readonly IClinicAppointmentSummarySender _summarySender;
    private readonly IOptions<HangfireOptions> _hangfire;
    private readonly IHostEnvironment _environment;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly TimeProvider _time;

    public StaffOperationsHealthService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IAppointmentReminderSender reminderSender,
        IClinicAppointmentSummarySender summarySender,
        IOptions<HangfireOptions> hangfire,
        IHostEnvironment environment,
        IAuthorizationAuditLogger audit,
        TimeProvider time)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _reminderSender = reminderSender;
        _summarySender = summarySender;
        _hangfire = hangfire;
        _environment = environment;
        _audit = audit;
        _time = time;
    }

    public async Task<StaffOperationsHealthResponse> GetHealthAsync(
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.Forbidden();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("operations_health", null, clinicId);
        }
        else if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        var clinic = await ResolveClinicForMetricsAsync(clinicId, bypass, cancellationToken);

        int? failedReminders = null;
        int? pendingReminders = null;
        int? failedSummaries = null;
        bool? missingAvailability = null;
        string? clinicName = null;
        Guid? resolvedClinicId = null;

        if (clinic is not null)
        {
            resolvedClinicId = clinic.Id;
            clinicName = clinic.Name;

            failedReminders = await (
                from reminder in _dbContext.AppointmentReminders.AsNoTracking()
                join appointment in _dbContext.Appointments.AsNoTracking()
                    on reminder.AppointmentId equals appointment.Id
                where reminder.Status == AppointmentReminderStatus.Failed
                    && appointment.ClinicId == clinic.Id
                select reminder.Id).CountAsync(cancellationToken);

            pendingReminders = await (
                from reminder in _dbContext.AppointmentReminders.AsNoTracking()
                join appointment in _dbContext.Appointments.AsNoTracking()
                    on reminder.AppointmentId equals appointment.Id
                where reminder.Status == AppointmentReminderStatus.Pending
                    && appointment.ClinicId == clinic.Id
                select reminder.Id).CountAsync(cancellationToken);

            failedSummaries = await _dbContext.ClinicAppointmentSummaryRuns.AsNoTracking()
                .CountAsync(
                    r => r.ClinicId == clinic.Id
                        && r.Status == ClinicAppointmentSummaryRunStatus.Failed,
                    cancellationToken);

            var activeDoctorCount = await _dbContext.StaffMembers.AsNoTracking()
                .CountAsync(
                    s => s.ClinicId == clinic.Id && s.IsActive && s.Role == AppRoles.Doctor,
                    cancellationToken);

            var hasAvailability = await _dbContext.DoctorAvailabilities.AsNoTracking()
                .AnyAsync(a => a.ClinicId == clinic.Id && a.IsActive, cancellationToken);

            missingAvailability = activeDoctorCount > 0 && !hasAvailability;
        }

        var options = _hangfire.Value;
        var response = new StaffOperationsHealthResponse
        {
            ReminderSenderMode = DescribeSender(_reminderSender),
            SummarySenderMode = DescribeSender(_summarySender),
            HangfireWorkersEnabled = options.Enabled,
            HangfireRecurringJobsScheduled = options.ScheduleRecurringJobs,
            HangfireDashboardEnabled = options.Dashboard.Enabled,
            HangfireQueues = options.Queues?.ToArray() ?? [],
            ClinicId = resolvedClinicId,
            ClinicName = clinicName,
            FailedReminderCount = failedReminders,
            PendingReminderCount = pendingReminders,
            FailedSummaryRunCount = failedSummaries,
            MissingActiveDoctorAvailability = missingAvailability,
            GeneratedAtUtc = _time.GetUtcNow(),
        };

        _audit.ReminderOperation(
            "operations_health",
            "succeeded",
            _currentStaff.HasActiveMembership ? _currentStaff.OrganizationId : clinic?.OrganizationId,
            resolvedClinicId ?? (_currentStaff.HasActiveMembership ? _currentStaff.ClinicId : null),
            reminderId: null);

        return response;
    }

    private async Task<ClinicMetricsScope?> ResolveClinicForMetricsAsync(
        Guid? requestedClinicId,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (requestedClinicId is not Guid clinicId || clinicId == Guid.Empty)
            {
                return null;
            }

            var clinic = await _dbContext.Clinics.AsNoTracking()
                .Where(c => c.Id == clinicId)
                .Select(c => new ClinicMetricsScope(c.Id, c.Name, c.OrganizationId))
                .SingleOrDefaultAsync(cancellationToken);

            return clinic;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            return null;
        }

        // Clinic-scoped staff (including CLINIC_ADMIN): always membership clinic.
        if (!_currentUser.IsInRole(AppRoles.OrganizationAdmin)
            && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return await LoadMembershipClinicAsync(cancellationToken);
        }

        // ORGANIZATION_ADMIN: optional clinic filter within trusted organization; no silent first-clinic.
        if (_currentUser.IsInRole(AppRoles.OrganizationAdmin))
        {
            if (requestedClinicId is not Guid filterId || filterId == Guid.Empty)
            {
                return null;
            }

            var clinic = await _dbContext.Clinics.AsNoTracking()
                .Where(c => c.Id == filterId && c.OrganizationId == _currentStaff.OrganizationId)
                .Select(c => new ClinicMetricsScope(c.Id, c.Name, c.OrganizationId))
                .SingleOrDefaultAsync(cancellationToken);

            if (clinic is null)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            return clinic;
        }

        return null;
    }

    private async Task<ClinicMetricsScope?> LoadMembershipClinicAsync(CancellationToken cancellationToken)
    {
        var clinicId = _currentStaff.ClinicId;
        if (clinicId == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => new ClinicMetricsScope(c.Id, c.Name, c.OrganizationId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private string DescribeSender(object sender)
    {
        var name = sender.GetType().Name;
        if (name.Contains("Development", StringComparison.OrdinalIgnoreCase))
        {
            return _environment.IsDevelopment() ? "Development" : "DevelopmentUnexpected";
        }

        if (name.Contains("NoOp", StringComparison.OrdinalIgnoreCase))
        {
            return "Disabled";
        }

        return "Configured";
    }

    private sealed record ClinicMetricsScope(Guid Id, string Name, Guid OrganizationId);
}
