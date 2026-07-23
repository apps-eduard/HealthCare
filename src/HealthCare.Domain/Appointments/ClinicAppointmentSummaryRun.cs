namespace HealthCare.Domain.Appointments;

public enum ClinicAppointmentSummaryRunStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
}

/// <summary>
/// Idempotent daily summary delivery tracking for a clinic calendar day.
/// Does not store message bodies or patient payloads.
/// </summary>
public sealed class ClinicAppointmentSummaryRun
{
    public const int MaxAttempts = 5;

    /// <summary>Clinic-local wall-clock time when the day's summary becomes due.</summary>
    public static readonly TimeOnly DefaultLocalSendTime = new(6, 0);

    public Guid Id { get; set; }

    public Guid ClinicId { get; set; }

    public Guid OrganizationId { get; set; }

    /// <summary>Clinic-local calendar date covered by the summary.</summary>
    public DateOnly SummaryDate { get; set; }

    public DateTimeOffset ScheduledAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ClinicAppointmentSummaryRunStatus Status { get; set; } = ClinicAppointmentSummaryRunStatus.Pending;

    public int AttemptCount { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastError { get; set; }

    /// <summary>Stable key: "{clinicId:N}:{yyyy-MM-dd}".</summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    public string? BackgroundJobId { get; set; }

    public int AppointmentCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public static string BuildIdempotencyKey(Guid clinicId, DateOnly summaryDate) =>
        $"{clinicId:N}:{summaryDate:yyyy-MM-dd}";
}
