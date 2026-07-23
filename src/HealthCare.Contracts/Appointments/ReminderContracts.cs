namespace HealthCare.Contracts.Appointments;

public static class AppointmentReminderErrorCodes
{
    public const string ReminderNotFound = "appointment.reminder_not_found";
    public const string ReminderAlreadySent = "appointment.reminder_already_sent";
    public const string ReminderNotRetryable = "appointment.reminder_not_retryable";
    public const string ReminderDeliveryFailed = "appointment.reminder_delivery_failed";
}

public sealed class AppointmentReminderResponse
{
    public Guid Id { get; init; }

    public string ReminderType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset ScheduledAtUtc { get; init; }

    public DateTimeOffset? SentAtUtc { get; init; }

    public int AttemptCount { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class RetryAppointmentReminderRequest
{
    public Guid ReminderId { get; init; }
}
