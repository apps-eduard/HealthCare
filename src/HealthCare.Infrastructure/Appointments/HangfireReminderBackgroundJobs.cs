using HealthCare.Application.Appointments;
using Hangfire;
using Hangfire.States;

namespace HealthCare.Infrastructure.Appointments;

public sealed class HangfireReminderBackgroundJobs : IReminderBackgroundJobs
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireReminderBackgroundJobs(IBackgroundJobClient jobs)
    {
        _jobs = jobs;
    }

    public string EnqueueProcess(Guid appointmentId, Guid reminderId) =>
        _jobs.Enqueue<AppointmentReminderHangfireJobs>(j =>
            j.ProcessReminderAsync(appointmentId, reminderId, CancellationToken.None));

    public string ScheduleProcess(Guid appointmentId, Guid reminderId, DateTimeOffset whenUtc)
    {
        var delay = whenUtc - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        return _jobs.Schedule<AppointmentReminderHangfireJobs>(
            j => j.ProcessReminderAsync(appointmentId, reminderId, CancellationToken.None),
            delay);
    }

    public void TryDelete(string? jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return;
        }

        _jobs.ChangeState(jobId, new DeletedState());
    }
}

/// <summary>
/// Hangfire entry points — arguments are IDs only.
/// </summary>
public sealed class AppointmentReminderHangfireJobs
{
    private readonly IAppointmentReminderProcessor _processor;
    private readonly IAppointmentReminderRecoveryService _recovery;

    public AppointmentReminderHangfireJobs(
        IAppointmentReminderProcessor processor,
        IAppointmentReminderRecoveryService recovery)
    {
        _processor = processor;
        _recovery = recovery;
    }

    [Queue("reminders")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public Task ProcessReminderAsync(Guid appointmentId, Guid reminderId, CancellationToken cancellationToken) =>
        _processor.ProcessReminderAsync(appointmentId, reminderId, cancellationToken);

    [Queue("default")]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 1)]
    public Task RecoverOverdueRemindersAsync(CancellationToken cancellationToken) =>
        _recovery.RecoverOverdueAsync(cancellationToken);
}

/// <summary>
/// Test/in-memory enqueuer that does not require Hangfire.
/// </summary>
public sealed class ImmediateReminderBackgroundJobs : IReminderBackgroundJobs
{
    public List<(Guid AppointmentId, Guid ReminderId, DateTimeOffset? When)> Enqueued { get; } = [];

    public string EnqueueProcess(Guid appointmentId, Guid reminderId)
    {
        Enqueued.Add((appointmentId, reminderId, null));
        return $"immediate:{reminderId:N}";
    }

    public string ScheduleProcess(Guid appointmentId, Guid reminderId, DateTimeOffset whenUtc)
    {
        Enqueued.Add((appointmentId, reminderId, whenUtc));
        return $"scheduled:{reminderId:N}";
    }

    public void TryDelete(string? jobId)
    {
        // no-op for tests
    }
}
