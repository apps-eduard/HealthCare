namespace HealthCare.Contracts.Clinics;

public static class ClinicAuditLogErrorCodes
{
    public const string AccessDenied = "clinic_audit.access_denied";
    public const string InvalidScope = "clinic_audit.invalid_scope";
    public const string ClinicScopeRequired = "clinic_audit.clinic_scope_required";
    public const string ClinicNotFound = "clinic_audit.clinic_not_found";
    public const string NotFound = "clinic_audit.not_found";
    public const string InvalidDateRange = "clinic_audit.invalid_date_range";
}

public sealed class ClinicAuditLogQuery
{
    /// <summary>Required for PLATFORM_ADMIN with explicit bypass. Ignored for clinic-scoped staff.</summary>
    public Guid? ClinicId { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? Category { get; init; }

    public string? Action { get; init; }

    public string? ResultCode { get; init; }

    public string? ResourceType { get; init; }

    public string? CorrelationId { get; init; }

    /// <summary>Inclusive clinic-local calendar date (yyyy-MM-dd).</summary>
    public string? FromDate { get; init; }

    /// <summary>Inclusive clinic-local calendar date (yyyy-MM-dd).</summary>
    public string? ToDate { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed class ClinicAuditLogListResponse
{
    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public Guid OrganizationId { get; init; }

    public required string TimeZoneId { get; init; }

    public int RetentionDays { get; init; }

    public int TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public IReadOnlyList<ClinicAuditLogItem> Items { get; init; } = [];
}

/// <summary>
/// Safe clinic audit list/detail item — no passwords, tokens, PHI, raw metadata, or request bodies.
/// </summary>
public sealed class ClinicAuditLogItem
{
    public Guid AuditLogId { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public required string Category { get; init; }

    public required string Action { get; init; }

    public required string ResultCode { get; init; }

    public Guid? ActorUserId { get; init; }

    /// <summary>Truncated safe actor reference (never a full secret).</summary>
    public string? ActorDisplayName { get; init; }

    public string? ActorRole { get; init; }

    public string? ResourceType { get; init; }

    public Guid? ResourceId { get; init; }

    public string? CorrelationId { get; init; }

    public Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public required string Summary { get; init; }
}

public sealed class ClinicAuditLogDetailResponse
{
    public required ClinicAuditLogItem Event { get; init; }

    public int RetentionDays { get; init; }

    public required string TimeZoneId { get; init; }
}

/// <summary>
/// Explicit allowlist of clinic-admin-visible audit actions (shared by API and Web filters).
/// </summary>
public static class ClinicAuditActions
{
    public static readonly string[] All =
    [
        "clinic_profile_update",
        "staff_created",
        "staff_updated",
        "staff_activated",
        "staff_deactivated",
        "staff_role_assigned",
        "staff_password_reset",
        "staff_sessions_revoked",
        "clinic_patient_enroll",
        "patient_clinic_status_changed",
        "appointment_requested",
        "appointment_created_by_staff",
        "appointment_confirmed",
        "appointment_cancelled",
        "appointment_checked_in",
        "appointment_completed",
        "appointment_no_show",
        "appointment_rescheduled",
        "availability_created",
        "availability_updated",
        "availability_deleted",
        "availability_exception_created",
        "availability_exception_deleted",
        "reminder_retry",
        "summary_retry",
        "operations_health",
        "report_appointments",
        "report_doctors",
        "report_patients",
        "report_reminders",
    ];

    public static string ToSummary(string action) => action.Trim() switch
    {
        "clinic_profile_update" => "Clinic profile updated",
        "staff_created" => "Staff member created",
        "staff_updated" => "Staff member updated",
        "staff_activated" => "Staff member activated",
        "staff_deactivated" => "Staff member deactivated",
        "staff_role_assigned" => "Staff role changed",
        "staff_password_reset" => "Staff password reset requested",
        "staff_sessions_revoked" => "Staff sessions revoked",
        "clinic_patient_enroll" => "Patient enrolled in clinic",
        "patient_clinic_status_changed" => "Patient clinic status changed",
        "appointment_requested" => "Appointment requested",
        "appointment_created_by_staff" => "Appointment created by staff",
        "appointment_confirmed" => "Appointment confirmed",
        "appointment_cancelled" => "Appointment cancelled",
        "appointment_checked_in" => "Appointment checked in",
        "appointment_completed" => "Appointment completed",
        "appointment_no_show" => "Appointment marked no-show",
        "appointment_rescheduled" => "Appointment rescheduled",
        "availability_created" => "Weekly availability added",
        "availability_updated" => "Weekly availability updated",
        "availability_deleted" => "Weekly availability removed",
        "availability_exception_created" => "Availability exception added",
        "availability_exception_deleted" => "Availability exception removed",
        "reminder_retry" => "Reminder retry requested",
        "summary_retry" => "Summary retry requested",
        "operations_health" => "Operations health viewed",
        "report_appointments" => "Clinic appointments report viewed",
        "report_doctors" => "Clinic doctors report viewed",
        "report_patients" => "Clinic patients report viewed",
        "report_reminders" => "Clinic operations report viewed",
        _ => "Clinic operational event",
    };
}
