namespace HealthCare.Domain.Appointments;

public enum AppointmentReminderType
{
    Confirmation = 0,
    Upcoming = 1,
    Cancellation = 2,
}

public enum AppointmentReminderStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3,
    Cancelled = 4,
}

/// <summary>
/// Persistent reminder schedule for an appointment. Hangfire jobs pass only IDs and reload this row.
/// </summary>
public sealed class AppointmentReminder
{
    public const int MaxAttempts = 5;

    public Guid Id { get; set; }

    public Guid AppointmentId { get; set; }

    public AppointmentReminderType ReminderType { get; set; }

    public DateTimeOffset ScheduledAtUtc { get; set; }

    public DateTimeOffset? SentAtUtc { get; set; }

    public AppointmentReminderStatus Status { get; set; } = AppointmentReminderStatus.Pending;

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>
    /// Stable key preventing duplicate reminders, e.g. "{appointmentId}:Confirmation".
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? BackgroundJobId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static string BuildIdempotencyKey(Guid appointmentId, AppointmentReminderType type) =>
        $"{appointmentId:N}:{type}";
}
