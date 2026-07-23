using HealthCare.Application.Appointments;
using Hangfire;
using Hangfire.States;

namespace HealthCare.Infrastructure.Appointments;

public sealed class HangfireClinicAppointmentSummaryJobs : IClinicAppointmentSummaryJobs
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireClinicAppointmentSummaryJobs(IBackgroundJobClient jobs)
    {
        _jobs = jobs;
    }

    public string EnqueueProcess(Guid runId) =>
        _jobs.Enqueue<ClinicAppointmentSummaryHangfireJobs>(j =>
            j.ProcessSummaryRunAsync(runId, CancellationToken.None));

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
public sealed class ClinicAppointmentSummaryHangfireJobs
{
    private readonly IClinicAppointmentSummaryDispatcher _dispatcher;
    private readonly IClinicAppointmentSummaryProcessor _processor;
    private readonly IClinicAppointmentSummaryRecoveryService _recovery;

    public ClinicAppointmentSummaryHangfireJobs(
        IClinicAppointmentSummaryDispatcher dispatcher,
        IClinicAppointmentSummaryProcessor processor,
        IClinicAppointmentSummaryRecoveryService recovery)
    {
        _dispatcher = dispatcher;
        _processor = processor;
        _recovery = recovery;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [AutomaticRetry(Attempts = 1)]
    public Task DispatchDueSummariesAsync(CancellationToken cancellationToken) =>
        _dispatcher.DispatchDueAsync(cancellationToken);

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 300])]
    public Task ProcessSummaryRunAsync(Guid runId, CancellationToken cancellationToken) =>
        _processor.ProcessRunAsync(runId, cancellationToken);

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 1)]
    public Task RecoverFailedSummariesAsync(CancellationToken cancellationToken) =>
        _recovery.RecoverAsync(cancellationToken);
}

/// <summary>
/// Test/in-memory enqueuer that does not require Hangfire.
/// </summary>
public sealed class ImmediateClinicAppointmentSummaryJobs : IClinicAppointmentSummaryJobs
{
    public List<Guid> Enqueued { get; } = [];

    public string EnqueueProcess(Guid runId)
    {
        Enqueued.Add(runId);
        return $"immediate-summary:{runId:N}";
    }

    public void TryDelete(string? jobId)
    {
        // no-op for tests
    }
}
