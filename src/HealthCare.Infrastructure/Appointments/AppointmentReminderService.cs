using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class AppointmentReminderService : IAppointmentReminderService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IReminderBackgroundJobs _jobs;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentReminderService> _logger;

    public AppointmentReminderService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IReminderBackgroundJobs jobs,
        IAuthorizationAuditLogger audit,
        TimeProvider timeProvider,
        ILogger<AppointmentReminderService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _jobs = jobs;
        _audit = audit;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AppointmentReminderResponse>> ListForAppointmentAsync(
        Guid appointmentId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var appointment = await EnsureStaffCanAccessAppointmentAsync(appointmentId, bypass, cancellationToken);

        var rows = await _dbContext.AppointmentReminders
            .AsNoTracking()
            .Where(r => r.AppointmentId == appointmentId)
            .OrderBy(r => r.ScheduledAtUtc)
            .ToListAsync(cancellationToken);

        _audit.ReminderOperation(
            "reminder_list_appointment",
            "succeeded",
            appointment.OrganizationId,
            appointment.ClinicId,
            reminderId: null);

        return rows.Select(r => Map(r, appointment.ClinicId)).ToList();
    }

    public async Task<PagedResponse<AppointmentReminderResponse>> SearchForStaffAsync(
        StaffReminderSearchQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveListScopeAsync(query.ClinicId, bypass, cancellationToken);

        var reminders =
            from r in _dbContext.AppointmentReminders.AsNoTracking()
            join a in _dbContext.Appointments.AsNoTracking() on r.AppointmentId equals a.Id
            select new { Reminder = r, Appointment = a };

        if (scope.Mode == ScopeMode.Clinic || scope.Mode == ScopeMode.Platform)
        {
            reminders = reminders.Where(x => x.Appointment.ClinicId == scope.ClinicId);
        }
        else
        {
            reminders = reminders.Where(x => x.Appointment.OrganizationId == scope.OrganizationId);
            if (scope.ClinicId.HasValue)
            {
                reminders = reminders.Where(x => x.Appointment.ClinicId == scope.ClinicId.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<AppointmentReminderStatus>(query.Status, ignoreCase: true, out var status))
        {
            reminders = reminders.Where(x => x.Reminder.Status == status);
        }

        if (query.FromUtc.HasValue)
        {
            reminders = reminders.Where(x => x.Reminder.ScheduledAtUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            reminders = reminders.Where(x => x.Reminder.ScheduledAtUtc <= query.ToUtc.Value);
        }

        var totalCount = await reminders.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1
            ? StaffReminderSearchQueryValidator.DefaultPageSize
            : Math.Min(query.PageSize, StaffReminderSearchQueryValidator.MaxPageSize);

        var rows = await reminders
            .OrderByDescending(x => x.Reminder.ScheduledAtUtc)
            .ThenBy(x => x.Reminder.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(x => Map(x.Reminder, x.Appointment.ClinicId))
            .ToList();

        _audit.ReminderOperation(
            "reminder_search",
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId,
            reminderId: null);

        return PagedResponse<AppointmentReminderResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<AppointmentReminderResponse> RetryAsync(
        Guid appointmentId,
        Guid reminderId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var appointment = await EnsureStaffCanAccessAppointmentAsync(appointmentId, bypass, cancellationToken);

        var reminder = await _dbContext.AppointmentReminders
            .SingleOrDefaultAsync(r => r.Id == reminderId && r.AppointmentId == appointmentId, cancellationToken)
            ?? throw AppointmentReminderException.NotFound();

        if (reminder.Status == AppointmentReminderStatus.Sent)
        {
            throw AppointmentReminderException.AlreadySent();
        }

        if (reminder.Status is not (AppointmentReminderStatus.Failed or AppointmentReminderStatus.Pending))
        {
            throw AppointmentReminderException.NotRetryable();
        }

        if (reminder.AttemptCount >= AppointmentReminder.MaxAttempts
            && reminder.Status == AppointmentReminderStatus.Failed)
        {
            // Allow one staff-initiated retry path by resetting to Pending once.
            reminder.AttemptCount = Math.Max(0, AppointmentReminder.MaxAttempts - 1);
        }

        reminder.Status = AppointmentReminderStatus.Pending;
        reminder.ScheduledAtUtc = _timeProvider.GetUtcNow();
        reminder.LastError = null;

        var jobId = _jobs.EnqueueProcess(appointmentId, reminder.Id);
        reminder.BackgroundJobId = jobId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reminder retried. UserId={UserId} AppointmentId={AppointmentId} ReminderId={ReminderId}",
            _currentUser.UserId,
            appointmentId,
            reminderId);
        _audit.ReminderOperation(
            "reminder_retry",
            "succeeded",
            appointment.OrganizationId,
            appointment.ClinicId,
            reminder.Id);

        return Map(reminder, appointment.ClinicId);
    }

    private async Task<AppointmentAccess> EnsureStaffCanAccessAppointmentAsync(
        Guid appointmentId,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            _audit.CrossTenantDenied(
                "reminder_patient_denied",
                Contracts.Identity.AuthorizationErrorCodes.Forbidden,
                null,
                null);
            throw AuthorizationException.Forbidden();
        }

        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw AppointmentException.NotFoundOrDenied();

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return new AppointmentAccess(appointment.OrganizationId, appointment.ClinicId);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            if (appointment.OrganizationId != _currentStaff.OrganizationId)
            {
                _audit.CrossTenantDenied(
                    "reminder_cross_org_denied",
                    Contracts.Identity.AuthorizationErrorCodes.Forbidden,
                    _currentStaff.OrganizationId,
                    appointment.ClinicId);
                throw AppointmentException.NotFoundOrDenied();
            }

            return new AppointmentAccess(appointment.OrganizationId, appointment.ClinicId);
        }

        if (appointment.ClinicId != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied(
                "reminder_cross_clinic_denied",
                Contracts.Identity.AuthorizationErrorCodes.Forbidden,
                _currentStaff.OrganizationId,
                appointment.ClinicId);
            throw AppointmentException.NotFoundOrDenied();
        }

        return new AppointmentAccess(appointment.OrganizationId, appointment.ClinicId);
    }

    private async Task<ListScope> ResolveListScopeAsync(
        Guid? clinicIdFilter,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.Forbidden();
        }

        if (_currentStaff.HasActiveMembership)
        {
            if (_currentStaff.Role == AppRoles.OrganizationAdmin)
            {
                Guid? clinicId = null;
                if (clinicIdFilter.HasValue)
                {
                    var clinic = await _dbContext.Clinics
                        .AsNoTracking()
                        .SingleOrDefaultAsync(c => c.Id == clinicIdFilter.Value, cancellationToken);
                    if (clinic is null || clinic.OrganizationId != _currentStaff.OrganizationId)
                    {
                        _audit.CrossTenantDenied(
                            "reminder_clinic_filter_denied",
                            Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                            _currentStaff.OrganizationId,
                            clinicIdFilter);
                        throw AuthorizationException.ClinicAccessDenied();
                    }

                    clinicId = clinic.Id;
                }

                return ListScope.ForOrganization(_currentStaff.OrganizationId, clinicId);
            }

            return ListScope.ForClinic(_currentStaff.OrganizationId, _currentStaff.ClinicId);
        }

        if (bypass == PlatformAdminBypass.Explicit
            && _currentUser.IsInRole(AppRoles.PlatformAdmin)
            && clinicIdFilter.HasValue)
        {
            var clinic = await _dbContext.Clinics
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == clinicIdFilter.Value, cancellationToken);
            if (clinic is null)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            return ListScope.ForPlatform(clinic.OrganizationId, clinic.Id);
        }

        throw AuthorizationException.MissingStaffMembership();
    }

    private static AppointmentReminderResponse Map(AppointmentReminder r, Guid clinicId) =>
        new()
        {
            Id = r.Id,
            AppointmentId = r.AppointmentId,
            ClinicId = clinicId,
            ReminderType = r.ReminderType.ToString(),
            Status = r.Status.ToString(),
            ScheduledAtUtc = r.ScheduledAtUtc,
            SentAtUtc = r.SentAtUtc,
            AttemptCount = r.AttemptCount,
            ErrorCode = string.IsNullOrWhiteSpace(r.LastError)
                ? null
                : AppointmentReminderErrorCodes.ReminderDeliveryFailed,
            ErrorMessage = string.IsNullOrWhiteSpace(r.LastError) ? null : r.LastError,
            BackgroundJobId = r.BackgroundJobId,
        };

    private sealed record AppointmentAccess(Guid OrganizationId, Guid ClinicId);

    private enum ScopeMode
    {
        Clinic,
        Organization,
        Platform,
    }

    private sealed class ListScope
    {
        private ListScope(ScopeMode mode, Guid organizationId, Guid? clinicId)
        {
            Mode = mode;
            OrganizationId = organizationId;
            ClinicId = clinicId;
        }

        public ScopeMode Mode { get; }

        public Guid OrganizationId { get; }

        public Guid? ClinicId { get; }

        public static ListScope ForClinic(Guid organizationId, Guid clinicId) =>
            new(ScopeMode.Clinic, organizationId, clinicId);

        public static ListScope ForOrganization(Guid organizationId, Guid? clinicId) =>
            new(ScopeMode.Organization, organizationId, clinicId);

        public static ListScope ForPlatform(Guid organizationId, Guid clinicId) =>
            new(ScopeMode.Platform, organizationId, clinicId);
    }
}
