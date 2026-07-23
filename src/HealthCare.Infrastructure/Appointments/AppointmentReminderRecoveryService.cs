using HealthCare.Application.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class AppointmentReminderRecoveryService : IAppointmentReminderRecoveryService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly IReminderBackgroundJobs _jobs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentReminderRecoveryService> _logger;

    public AppointmentReminderRecoveryService(
        HealthCareDbContext dbContext,
        IReminderBackgroundJobs jobs,
        TimeProvider timeProvider,
        ILogger<AppointmentReminderRecoveryService> logger)
    {
        _dbContext = dbContext;
        _jobs = jobs;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RecoverOverdueAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var overdue = await _dbContext.AppointmentReminders
            .Where(r => r.Status == AppointmentReminderStatus.Pending
                        && r.ScheduledAtUtc <= now
                        && r.AttemptCount < AppointmentReminder.MaxAttempts)
            .OrderBy(r => r.ScheduledAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var reminder in overdue)
        {
            var jobId = _jobs.EnqueueProcess(reminder.AppointmentId, reminder.Id);
            reminder.BackgroundJobId = jobId;
            _logger.LogInformation(
                "Reminder requeued by recovery. AppointmentId={AppointmentId} ReminderId={ReminderId}",
                reminder.AppointmentId,
                reminder.Id);
        }

        if (overdue.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
