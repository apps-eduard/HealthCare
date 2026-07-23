namespace HealthCare.Contracts.Appointments;

public static class AppointmentErrorCodes
{
    public const string NotEnrolled = "appointment.not_enrolled";
    public const string InactivePatient = "appointment.inactive_patient";
    public const string InactiveClinic = "appointment.inactive_clinic";
    public const string InvalidAssignedStaff = "appointment.invalid_assigned_staff";
    public const string NotFoundOrDenied = "appointment.not_found_or_denied";
    public const string InvalidTransition = "appointment.invalid_transition";
    public const string SlotConflict = "appointment.slot_conflict";
    public const string ConcurrencyConflict = "appointment.concurrency_conflict";
    public const string InvalidTime = "appointment.invalid_time";
    public const string InvalidRequest = "appointment.invalid_request";
    public const string RescheduleNotAllowed = "appointment.reschedule_not_allowed";
    public const string RescheduleSameSlot = "appointment.reschedule_same_slot";
    public const string RescheduleFailed = "appointment.reschedule_failed";
}

public sealed class RescheduleAppointmentRequest
{
    /// <summary>
    /// Optional. When omitted or empty, the current doctor is preserved.
    /// </summary>
    public Guid? DoctorStaffMemberId { get; init; }

    public DateTimeOffset AppointmentDateUtc { get; init; }

    public int DurationMinutes { get; init; }

    public int ExpectedVersion { get; init; }

    public string? Reason { get; init; }
}

public sealed class CreatePatientAppointmentRequest
{
    public string ClinicCode { get; init; } = string.Empty;

    public Guid DoctorStaffMemberId { get; init; }

    public DateTimeOffset AppointmentDateUtc { get; init; }

    public int DurationMinutes { get; init; } = 30;

    public string? Reason { get; init; }

    public string? PatientNotes { get; init; }
}

public sealed class CreateStaffAppointmentRequest
{
    public Guid PatientId { get; init; }

    public Guid DoctorStaffMemberId { get; init; }

    public DateTimeOffset AppointmentDateUtc { get; init; }

    public int DurationMinutes { get; init; } = 30;

    public string? Reason { get; init; }

    public string? PatientNotes { get; init; }

    /// <summary>
    /// Required for ORGANIZATION_ADMIN (in-org clinic) and PLATFORM_ADMIN (with bypass).
    /// Ignored for clinic-scoped staff (trusted membership clinic is used).
    /// </summary>
    public Guid? ClinicId { get; init; }
}

public sealed class AppointmentListQuery
{
    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public string? Status { get; init; }

    public Guid? DoctorStaffMemberId { get; init; }

    public Guid? ClinicId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string SortBy { get; init; } = "appointmentDateUtc";

    public string SortDirection { get; init; } = "asc";
}

public sealed class AppointmentActionRequest
{
    public int ExpectedVersion { get; init; }

    public string? CancellationReason { get; init; }
}

public sealed class AppointmentResponse
{
    public Guid Id { get; init; }

    public Guid OrganizationId { get; init; }

    public Guid ClinicId { get; init; }

    public Guid PatientId { get; init; }

    public Guid ClinicPatientId { get; init; }

    public Guid DoctorStaffMemberId { get; init; }

    public DateTimeOffset AppointmentDateUtc { get; init; }

    public int DurationMinutes { get; init; }

    public DateTimeOffset EndsAtUtc { get; init; }

    public string? Reason { get; init; }

    public string Status { get; init; } = string.Empty;

    public string? PatientNotes { get; init; }

    public string? CancellationReason { get; init; }

    public string Source { get; init; } = string.Empty;

    public int Version { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Safe display fields for staff UI (not medical notes).</summary>
    public string? PatientDisplayName { get; init; }

    public string? LocalPatientNumber { get; init; }

    public string? DoctorDisplayName { get; init; }

    public string? ClinicName { get; init; }

    public string? ClinicSlug { get; init; }

    public string? ClinicTimeZoneId { get; init; }
}
