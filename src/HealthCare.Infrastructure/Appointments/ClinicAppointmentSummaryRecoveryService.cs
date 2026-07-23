using HealthCare.Application.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class ClinicAppointmentSummaryRecoveryService : IClinicAppointmentSummaryRecoveryService
{
    public static readonly TimeSpan StuckProcessingThreshold = TimeSpan.FromMinutes(30);

    private readonly HealthCareDbContext _dbContext;
    private readonly IClinicAppointmentSummaryJobs _jobs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicAppointmentSummaryRecoveryService> _logger;

    public ClinicAppointmentSummaryRecoveryService(
        HealthCareDbContext dbContext,
        IClinicAppointmentSummaryJobs jobs,
        TimeProvider timeProvider,
        ILogger<ClinicAppointmentSummaryRecoveryService> logger)
    {
        _dbContext = dbContext;
        _jobs = jobs;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var stuckBefore = now - StuckProcessingThreshold;

        var eligible = await _dbContext.ClinicAppointmentSummaryRuns
            .Where(r => r.AttemptCount < ClinicAppointmentSummaryRun.MaxAttempts
                        && (r.Status == ClinicAppointmentSummaryRunStatus.Failed
                            || (r.Status == ClinicAppointmentSummaryRunStatus.Processing
                                && r.StartedAtUtc != null
                                && r.StartedAtUtc < stuckBefore)
                            || (r.Status == ClinicAppointmentSummaryRunStatus.Pending
                                && r.ScheduledAtUtc <= now
                                && r.BackgroundJobId == null)))
            .OrderBy(r => r.ScheduledAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var run in eligible)
        {
            if (run.Status == ClinicAppointmentSummaryRunStatus.Completed)
            {
                continue;
            }

            run.Status = ClinicAppointmentSummaryRunStatus.Pending;
            run.LastError = null;
            var jobId = _jobs.EnqueueProcess(run.Id);
            run.BackgroundJobId = jobId;

            _logger.LogInformation(
                "Summary retried by recovery. ClinicId={ClinicId} SummaryDate={SummaryDate} RunId={RunId}",
                run.ClinicId,
                run.SummaryDate,
                run.Id);
        }

        if (eligible.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
