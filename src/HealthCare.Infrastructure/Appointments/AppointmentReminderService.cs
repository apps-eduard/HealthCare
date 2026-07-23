using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentReminderService> _logger;

    public AppointmentReminderService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IReminderBackgroundJobs jobs,
        TimeProvider timeProvider,
        ILogger<AppointmentReminderService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _jobs = jobs;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AppointmentReminderResponse>> ListForAppointmentAsync(
        Guid appointmentId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        await EnsureStaffCanAccessAppointmentAsync(appointmentId, bypass, cancellationToken);

        var rows = await _dbContext.AppointmentReminders
            .AsNoTracking()
            .Where(r => r.AppointmentId == appointmentId)
            .OrderBy(r => r.ScheduledAtUtc)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<AppointmentReminderResponse> RetryAsync(
        Guid appointmentId,
        Guid reminderId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        await EnsureStaffCanAccessAppointmentAsync(appointmentId, bypass, cancellationToken);

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

        return Map(reminder);
    }

    private async Task EnsureStaffCanAccessAppointmentAsync(
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
            _logger.LogInformation(
                "Cross-tenant reminder access denied. UserId={UserId} AppointmentId={AppointmentId} Reason=patient",
                _currentUser.UserId,
                appointmentId);
            throw AuthorizationException.Forbidden();
        }

        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken)
            ?? throw AppointmentException.NotFoundOrDenied();

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            if (appointment.OrganizationId != _currentStaff.OrganizationId)
            {
                _logger.LogInformation(
                    "Cross-tenant reminder access denied. UserId={UserId} AppointmentId={AppointmentId}",
                    _currentUser.UserId,
                    appointmentId);
                throw AppointmentException.NotFoundOrDenied();
            }

            return;
        }

        if (appointment.ClinicId != _currentStaff.ClinicId)
        {
            _logger.LogInformation(
                "Cross-tenant reminder access denied. UserId={UserId} AppointmentId={AppointmentId}",
                _currentUser.UserId,
                appointmentId);
            throw AppointmentException.NotFoundOrDenied();
        }
    }

    private static AppointmentReminderResponse Map(AppointmentReminder r) =>
        new()
        {
            Id = r.Id,
            ReminderType = r.ReminderType.ToString(),
            Status = r.Status.ToString(),
            ScheduledAtUtc = r.ScheduledAtUtc,
            SentAtUtc = r.SentAtUtc,
            AttemptCount = r.AttemptCount,
            ErrorCode = string.IsNullOrWhiteSpace(r.LastError) ? null : AppointmentReminderErrorCodes.ReminderDeliveryFailed,
            ErrorMessage = string.IsNullOrWhiteSpace(r.LastError) ? null : r.LastError,
        };
}
