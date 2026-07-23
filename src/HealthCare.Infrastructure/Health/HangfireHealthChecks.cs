using HealthCare.Infrastructure.Configuration;
using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Health;

/// <summary>
/// Checks Hangfire PostgreSQL storage connectivity. Used by readiness when workers or scheduling are enabled.
/// </summary>
public sealed class HangfireStorageHealthCheck : IHealthCheck
{
    private readonly IOptions<HangfireOptions> _options;

    public HangfireStorageHealthCheck(IOptions<HangfireOptions> options)
    {
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled && !opts.ScheduleRecurringJobs)
        {
            // Intentionally disabled — do not fail readiness/liveness for unused Hangfire storage.
            return Task.FromResult(HealthCheckResult.Healthy(
                "Hangfire workers and scheduling disabled; storage connectivity not required."));
        }

        try
        {
            var storage = JobStorage.Current;
            using var connection = storage.GetConnection();
            _ = connection.GetRecurringJobs();

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Hangfire storage reachable. WorkersEnabled={opts.Enabled} ScheduleRecurringJobs={opts.ScheduleRecurringJobs}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage unreachable.", ex));
        }
    }
}

/// <summary>
/// Reports whether the local Hangfire worker is enabled. Never fails when intentionally disabled.
/// </summary>
public sealed class HangfireWorkerStateHealthCheck : IHealthCheck
{
    private readonly IOptions<HangfireOptions> _options;

    public HangfireWorkerStateHealthCheck(IOptions<HangfireOptions> options)
    {
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        if (!opts.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Hangfire workers intentionally disabled. API may still enqueue jobs for an external worker."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Hangfire workers enabled. WorkerCount={opts.WorkerCount} Queues={string.Join(',', opts.Queues)} ServerName={opts.ServerName}"));
    }
}
