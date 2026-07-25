namespace HealthCare.Contracts.Clinics;

public static class ClinicReportErrorCodes
{
    public const string AccessDenied = "clinic_reports.access_denied";
    public const string InvalidScope = "clinic_reports.invalid_scope";
    public const string ClinicScopeRequired = "clinic_reports.clinic_scope_required";
    public const string ClinicNotFound = "clinic_reports.clinic_not_found";
    public const string InvalidDateRange = "clinic_reports.invalid_date_range";
}

public sealed class ClinicReportQuery
{
    /// <summary>Required for PLATFORM_ADMIN with explicit bypass. Ignored for clinic-scoped staff.</summary>
    public Guid? ClinicId { get; init; }

    /// <summary>Inclusive clinic-local calendar date (yyyy-MM-dd).</summary>
    public string? FromDate { get; init; }

    /// <summary>Inclusive clinic-local calendar date (yyyy-MM-dd).</summary>
    public string? ToDate { get; init; }
}

public sealed class ClinicReportContext
{
    public Guid ClinicId { get; init; }

    public string ClinicName { get; init; } = string.Empty;

    public Guid OrganizationId { get; init; }

    public string FromDate { get; init; } = string.Empty;

    public string ToDate { get; init; } = string.Empty;

    public string TimeZoneId { get; init; } = string.Empty;

    public string TimeZoneStrategy { get; init; } = "clinic";
}

public sealed class ClinicAppointmentStatusCount
{
    public string Status { get; init; } = string.Empty;

    public int Count { get; init; }

    public decimal PercentageOfTotal { get; init; }
}

public sealed class ClinicAppointmentVolumeDay
{
    public string LocalDate { get; init; } = string.Empty;

    public int AppointmentCount { get; init; }

    public int CompletedCount { get; init; }

    public int CancelledCount { get; init; }

    public int NoShowCount { get; init; }
}

public sealed class ClinicCancellationNoShowSummary
{
    public int CancelledByClinicCount { get; init; }

    public int CancelledByPatientCount { get; init; }

    public int NoShowCount { get; init; }

    public int TotalAppointments { get; init; }

    public decimal CancellationRate { get; init; }

    public decimal NoShowRate { get; init; }
}

public sealed class ClinicAppointmentReportResponse
{
    public ClinicReportContext Context { get; init; } = new();

    public int TotalAppointments { get; init; }

    public IReadOnlyList<ClinicAppointmentStatusCount> ByStatus { get; init; } = [];

    public IReadOnlyList<ClinicAppointmentVolumeDay> VolumeByDate { get; init; } = [];

    public ClinicCancellationNoShowSummary CancellationNoShow { get; init; } = new();
}

public sealed class ClinicDoctorAppointmentRow
{
    public Guid DoctorStaffMemberId { get; init; }

    public string DoctorDisplayName { get; init; } = string.Empty;

    public int TotalAppointments { get; init; }

    public int CompletedCount { get; init; }

    public int CancelledCount { get; init; }

    public int NoShowCount { get; init; }
}

public sealed class ClinicDoctorAppointmentsReportResponse
{
    public ClinicReportContext Context { get; init; } = new();

    public IReadOnlyList<ClinicDoctorAppointmentRow> Doctors { get; init; } = [];
}

public sealed class ClinicPatientEnrollmentReportResponse
{
    public ClinicReportContext Context { get; init; } = new();

    public int ActiveEnrollmentCount { get; init; }

    public int InactiveEnrollmentCount { get; init; }

    public int TotalClinicPatients { get; init; }

    /// <summary>Enrollments whose RegisteredAtUtc falls in the clinic-local report range.</summary>
    public int NewEnrollmentsInRange { get; init; }
}

public sealed class ClinicOperationsReportResponse
{
    public ClinicReportContext Context { get; init; } = new();

    public int PendingReminderCount { get; init; }

    public int ProcessingReminderCount { get; init; }

    public int SentReminderCount { get; init; }

    public int FailedReminderCount { get; init; }

    public int CancelledReminderCount { get; init; }

    public int FailedSummaryRunCount { get; init; }

    public int PendingSummaryRunCount { get; init; }

    public bool MissingActiveDoctorAvailability { get; init; }
}
