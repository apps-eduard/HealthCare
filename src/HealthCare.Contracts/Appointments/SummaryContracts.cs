namespace HealthCare.Contracts.Appointments;

public static class AppointmentSummaryErrorCodes
{
    public const string SummaryNotFound = "appointment.summary_not_found";
    public const string SummaryAlreadyCompleted = "appointment.summary_already_completed";
    public const string SummaryNotRetryable = "appointment.summary_not_retryable";
    public const string SummaryGenerationFailed = "appointment.summary_generation_failed";
    public const string SummaryDeliveryFailed = "appointment.summary_delivery_failed";
    public const string SummaryInvalidDate = "appointment.summary_invalid_date";
}

public sealed class ClinicAppointmentSummaryQuery
{
    /// <summary>Optional clinic-local date (yyyy-MM-dd). Defaults to today in the clinic timezone.</summary>
    public string? Date { get; init; }

    /// <summary>Org admin / platform admin may target a clinic; ignored for clinic-scoped staff.</summary>
    public Guid? ClinicId { get; init; }
}

public sealed class ClinicAppointmentSummaryResponse
{
    public Guid ClinicId { get; init; }

    public Guid OrganizationId { get; init; }

    public string ClinicCode { get; init; } = string.Empty;

    public string ClinicName { get; init; } = string.Empty;

    public string TimeZoneId { get; init; } = string.Empty;

    /// <summary>Clinic-local calendar date covered (yyyy-MM-dd).</summary>
    public string SummaryDate { get; init; } = string.Empty;

    public int TotalAppointments { get; init; }

    public int Requested { get; init; }

    public int Confirmed { get; init; }

    public int CheckedIn { get; init; }

    public int InProgress { get; init; }

    public int Completed { get; init; }

    public int NoShow { get; init; }

    public int CancelledByPatient { get; init; }

    public int CancelledByClinic { get; init; }

    public int UnassignedAppointments { get; init; }

    public DateTimeOffset? FirstAppointmentUtc { get; init; }

    public DateTimeOffset? LastAppointmentUtc { get; init; }

    public string? FirstAppointmentLocal { get; init; }

    public string? LastAppointmentLocal { get; init; }

    public IReadOnlyList<ClinicAppointmentSummaryDoctorGroup> ByDoctor { get; init; } = [];

    /// <summary>Minimal operational list — no patient profile or clinical content.</summary>
    public IReadOnlyList<ClinicAppointmentSummaryItem> Appointments { get; init; } = [];
}

public sealed class ClinicAppointmentSummaryDoctorGroup
{
    public Guid? DoctorStaffMemberId { get; init; }

    public string DoctorDisplayName { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed class ClinicAppointmentSummaryItem
{
    public Guid AppointmentId { get; init; }

    public string LocalTime { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string DoctorDisplayName { get; init; } = string.Empty;
}

public sealed class ClinicAppointmentSummaryRunQuery
{
    /// <summary>
    /// Optional clinic filter for ORGANIZATION_ADMIN (must belong to trusted organization)
    /// or PLATFORM_ADMIN with explicit bypass. Ignored for clinic-scoped staff.
    /// </summary>
    public Guid? ClinicId { get; init; }

    /// <summary>Allowed: Pending, Processing, Completed, Failed.</summary>
    public string? Status { get; init; }

    public string? FromDate { get; init; }

    public string? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed class ClinicAppointmentSummaryRunResponse
{
    public Guid RunId { get; init; }

    public Guid ClinicId { get; init; }

    public Guid OrganizationId { get; init; }

    public string SummaryDate { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int AttemptCount { get; init; }

    public int AppointmentCount { get; init; }

    public string? LastErrorCode { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset ScheduledAtUtc { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>Hangfire job correlation id only — never job arguments or payloads.</summary>
    public string? BackgroundJobId { get; init; }
}
