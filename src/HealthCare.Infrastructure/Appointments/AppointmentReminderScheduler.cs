using HealthCare.Application.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class AppointmentReminderScheduler : IAppointmentReminderScheduler
{
    public static readonly TimeSpan UpcomingLeadTime = TimeSpan.FromHours(24);

    private readonly HealthCareDbContext _dbContext;
    private readonly IReminderBackgroundJobs _jobs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentReminderScheduler> _logger;

    public AppointmentReminderScheduler(
        HealthCareDbContext dbContext,
        IReminderBackgroundJobs jobs,
        TimeProvider timeProvider,
        ILogger<AppointmentReminderScheduler> logger)
    {
        _dbContext = dbContext;
        _jobs = jobs;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ScheduleAfterAppointmentCreatedAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAppointmentAsync(appointmentId, cancellationToken);
        if (appointment is null || AppointmentStatusTransitions.IsCancelled(appointment.Status))
        {
            return;
        }

        await EnsureReminderAsync(appointment, AppointmentReminderType.Confirmation, _timeProvider.GetUtcNow(), cancellationToken);
        await EnsureUpcomingAsync(appointment, cancellationToken);
    }

    public async Task ScheduleAfterAppointmentConfirmedAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAppointmentAsync(appointmentId, cancellationToken);
        if (appointment is null
            || appointment.Status != AppointmentStatus.Confirmed
            || AppointmentStatusTransitions.IsCancelled(appointment.Status))
        {
            return;
        }

        await EnsureReminderAsync(appointment, AppointmentReminderType.Confirmation, _timeProvider.GetUtcNow(), cancellationToken);
        await EnsureUpcomingAsync(appointment, cancellationToken);
    }

    public async Task ScheduleAfterAppointmentCancelledAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAppointmentAsync(appointmentId, cancellationToken);
        if (appointment is null)
        {
            return;
        }

        await CancelPendingAsync(appointmentId, cancellationToken);
        await EnsureReminderAsync(appointment, AppointmentReminderType.Cancellation, _timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task ScheduleAfterAppointmentRescheduledAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAppointmentAsync(appointmentId, cancellationToken);
        if (appointment is null || AppointmentStatusTransitions.IsCancelled(appointment.Status))
        {
            return;
        }

        // Confirmation: leave existing row alone (never resend Sent Confirmation).
        await ReplaceUpcomingForNewScheduleAsync(appointment, cancellationToken);
    }

    private async Task ReplaceUpcomingForNewScheduleAsync(
        Appointment appointment,
        CancellationToken cancellationToken)
    {
        var when = appointment.AppointmentDateUtc - UpcomingLeadTime;
        var now = _timeProvider.GetUtcNow();
        if (when < now)
        {
            when = now;
        }

        var key = AppointmentReminder.BuildIdempotencyKey(appointment.Id, AppointmentReminderType.Upcoming);
        var existing = await _dbContext.AppointmentReminders
            .SingleOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);

        if (existing is null)
        {
            await EnsureReminderAsync(appointment, AppointmentReminderType.Upcoming, when, cancellationToken);
            _logger.LogInformation(
                "Upcoming reminder created after reschedule. AppointmentId={AppointmentId}",
                appointment.Id);
            return;
        }

        _jobs.TryDelete(existing.BackgroundJobId);

        existing.ScheduledAtUtc = when;
        existing.Status = AppointmentReminderStatus.Pending;
        existing.SentAtUtc = null;
        existing.AttemptCount = 0;
        existing.LastError = null;
        existing.BackgroundJobId = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var jobId = when <= _timeProvider.GetUtcNow()
            ? _jobs.EnqueueProcess(appointment.Id, existing.Id)
            : _jobs.ScheduleProcess(appointment.Id, existing.Id, when);

        existing.BackgroundJobId = jobId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upcoming reminder replaced after reschedule. AppointmentId={AppointmentId} ReminderId={ReminderId} ScheduledAtUtc={ScheduledAtUtc}",
            appointment.Id,
            existing.Id,
            when);
    }

    private async Task EnsureUpcomingAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        var when = appointment.AppointmentDateUtc - UpcomingLeadTime;
        var now = _timeProvider.GetUtcNow();
        if (when < now)
        {
            when = now;
        }

        await EnsureReminderAsync(appointment, AppointmentReminderType.Upcoming, when, cancellationToken);
    }

    private async Task EnsureReminderAsync(
        Appointment appointment,
        AppointmentReminderType type,
        DateTimeOffset scheduledAtUtc,
        CancellationToken cancellationToken)
    {
        var key = AppointmentReminder.BuildIdempotencyKey(appointment.Id, type);
        var existing = await _dbContext.AppointmentReminders
            .SingleOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);

        if (existing is not null)
        {
            return;
        }

        var reminder = new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            ReminderType = type,
            ScheduledAtUtc = scheduledAtUtc,
            Status = AppointmentReminderStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = key,
        };

        _dbContext.AppointmentReminders.Add(reminder);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Concurrent duplicate insert — idempotent.
            _dbContext.Entry(reminder).State = EntityState.Detached;
            _logger.LogInformation(
                "Reminder schedule skipped (duplicate). AppointmentId={AppointmentId} Type={ReminderType}",
                appointment.Id,
                type);
            return;
        }

        var jobId = scheduledAtUtc <= _timeProvider.GetUtcNow()
            ? _jobs.EnqueueProcess(appointment.Id, reminder.Id)
            : _jobs.ScheduleProcess(appointment.Id, reminder.Id, scheduledAtUtc);

        reminder.BackgroundJobId = jobId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reminder scheduled. AppointmentId={AppointmentId} ReminderId={ReminderId} Type={ReminderType} ScheduledAtUtc={ScheduledAtUtc}",
            appointment.Id,
            reminder.Id,
            type,
            scheduledAtUtc);
    }

    private async Task CancelPendingAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var pending = await _dbContext.AppointmentReminders
            .Where(r => r.AppointmentId == appointmentId
                        && (r.Status == AppointmentReminderStatus.Pending
                            || r.Status == AppointmentReminderStatus.Processing
                            || r.Status == AppointmentReminderStatus.Failed)
                        && r.ReminderType != AppointmentReminderType.Cancellation)
            .ToListAsync(cancellationToken);

        foreach (var reminder in pending)
        {
            _jobs.TryDelete(reminder.BackgroundJobId);
            reminder.Status = AppointmentReminderStatus.Cancelled;
            reminder.LastError = null;
            _logger.LogInformation(
                "Reminder cancelled. AppointmentId={AppointmentId} ReminderId={ReminderId} Type={ReminderType}",
                appointmentId,
                reminder.Id,
                reminder.ReminderType);
        }

        if (pending.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private Task<Appointment?> LoadAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken) =>
        _dbContext.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
}
