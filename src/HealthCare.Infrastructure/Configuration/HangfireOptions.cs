using System.ComponentModel.DataAnnotations;

namespace HealthCare.Infrastructure.Configuration;

public sealed class HangfireOptions
{
    public const string SectionName = "Hangfire";

    public const int MinWorkerCount = 1;

    public const int MaxWorkerCount = 64;

    public const int DefaultWorkerCount = 2;

    public const int MinShutdownTimeoutSeconds = 5;

    public const int MaxShutdownTimeoutSeconds = 300;

    /// <summary>
    /// When true, registers a local Hangfire Server (workers). Independent of dashboard.
    /// Outside Development this defaults to false unless explicitly enabled.
    /// </summary>
    public bool Enabled { get; set; }

    public int WorkerCount { get; set; } = DefaultWorkerCount;

    public string[] Queues { get; set; } = ["default", "reminders", "summaries"];

    public string ServerName { get; set; } = "healthcare-api";

    /// <summary>
    /// When true, registers/updates recurring jobs. Requires Hangfire storage.
    /// The API may enqueue jobs even when local workers are disabled (external worker compatible).
    /// </summary>
    public bool ScheduleRecurringJobs { get; set; }

    public int ShutdownTimeoutSeconds { get; set; } = 30;

    public HangfireDashboardOptions Dashboard { get; set; } = new();
}

public sealed class HangfireDashboardOptions
{
    /// <summary>
    /// Dashboard is independent of worker hosting. Default false outside Development.
    /// </summary>
    public bool Enabled { get; set; }

    public string Path { get; set; } = "/hangfire";
}
