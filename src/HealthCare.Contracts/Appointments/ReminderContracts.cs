namespace HealthCare.Contracts.Appointments;

public static class AppointmentReminderErrorCodes
{
    public const string ReminderNotFound = "appointment.reminder_not_found";
    public const string ReminderAlreadySent = "appointment.reminder_already_sent";
    public const string ReminderNotRetryable = "appointment.reminder_not_retryable";
    public const string ReminderDeliveryFailed = "appointment.reminder_delivery_failed";
    public const string InvalidSearch = "appointment.reminder_invalid_search";
}

public sealed class StaffReminderSearchQuery
{
    /// <summary>
    /// Optional clinic filter for ORGANIZATION_ADMIN (must belong to trusted organization)
    /// or PLATFORM_ADMIN with explicit bypass. Ignored for clinic-scoped staff.
    /// </summary>
    public Guid? ClinicId { get; init; }

    /// <summary>Allowed: Pending, Processing, Sent, Failed, Cancelled.</summary>
    public string? Status { get; init; }

    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed class AppointmentReminderResponse
{
    public Guid Id { get; init; }

    public Guid AppointmentId { get; init; }

    public Guid ClinicId { get; init; }

    public string ReminderType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset ScheduledAtUtc { get; init; }

    public DateTimeOffset? SentAtUtc { get; init; }

    public int AttemptCount { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>Hangfire job correlation id only — never job arguments or payloads.</summary>
    public string? BackgroundJobId { get; init; }
}

public sealed class RetryAppointmentReminderRequest
{
    public Guid ReminderId { get; init; }
}

/// <summary>
/// Safe operational health for reminder/summary delivery. No secrets or connection strings.
/// </summary>
public sealed class StaffOperationsHealthResponse
{
    public string ReminderSenderMode { get; init; } = string.Empty;

    public string SummarySenderMode { get; init; } = string.Empty;

    public bool HangfireWorkersEnabled { get; init; }

    public bool HangfireRecurringJobsScheduled { get; init; }

    public bool HangfireDashboardEnabled { get; init; }

    public IReadOnlyList<string> HangfireQueues { get; init; } = [];
}
