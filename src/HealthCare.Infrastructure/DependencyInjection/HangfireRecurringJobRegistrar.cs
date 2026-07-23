using HealthCare.Infrastructure.Appointments;
using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.DependencyInjection;

/// <summary>
/// Idempotent recurring Hangfire job registration. Safe to call from API or a future worker host.
/// </summary>
public static class HangfireRecurringJobRegistrar
{
    public const string ReminderRecoveryJobId = "appointment-reminder-recovery";
    public const string SummaryDispatchJobId = "clinic-appointment-summary-dispatch";
    public const string SummaryRecoveryJobId = "clinic-appointment-summary-recovery";

    public static IReadOnlyList<string> Register(
        IRecurringJobManager recurring,
        ILogger logger)
    {
        recurring.AddOrUpdate<AppointmentReminderHangfireJobs>(
            ReminderRecoveryJobId,
            j => j.RecoverOverdueRemindersAsync(CancellationToken.None),
            "*/5 * * * *");

        recurring.AddOrUpdate<ClinicAppointmentSummaryHangfireJobs>(
            SummaryDispatchJobId,
            j => j.DispatchDueSummariesAsync(CancellationToken.None),
            "*/15 * * * *");

        recurring.AddOrUpdate<ClinicAppointmentSummaryHangfireJobs>(
            SummaryRecoveryJobId,
            j => j.RecoverFailedSummariesAsync(CancellationToken.None),
            "*/15 * * * *");

        var registered = new[] { ReminderRecoveryJobId, SummaryDispatchJobId, SummaryRecoveryJobId };
        logger.LogInformation(
            "Hangfire recurring jobs registered. Jobs={Jobs}",
            string.Join(',', registered));
        return registered;
    }

    public static bool IsDesignTime(IHostEnvironment environment)
    {
        // EF design-time / tooling hosts should not mutate recurring jobs.
        if (string.Equals(
                Environment.GetEnvironmentVariable("EF_DESIGN_TIME"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var appName = environment.ApplicationName ?? string.Empty;
        return appName.Contains("ef", StringComparison.OrdinalIgnoreCase)
               && (appName.Contains("design", StringComparison.OrdinalIgnoreCase)
                   || appName.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
    }
}
