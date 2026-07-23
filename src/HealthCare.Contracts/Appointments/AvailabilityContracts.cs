namespace HealthCare.Contracts.Appointments;

public static class AvailabilityErrorCodes
{
    public const string OutsideAvailability = "appointment.outside_availability";
    public const string SlotUnavailable = "appointment.slot_unavailable";
    public const string AvailabilityException = "appointment.availability_exception";
    public const string InvalidSlotDuration = "appointment.invalid_slot_duration";
    public const string InvalidAvailability = "appointment.invalid_availability";
    public const string AvailabilityConflict = "appointment.availability_conflict";
    public const string AvailabilityConcurrency = "appointment.availability_concurrency_conflict";
    public const string InvalidTimeZone = "appointment.invalid_timezone";
    public const string DoctorNotFound = "appointment.doctor_not_found";
}

public sealed class ClinicDoctorResponse
{
    public Guid StaffMemberId { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string? Specialty { get; init; }

    public string ClinicCode { get; init; } = string.Empty;

    public bool AcceptsBookings { get; init; }
}

public sealed class DoctorAvailabilityResponse
{
    public Guid Id { get; init; }

    public Guid ClinicId { get; init; }

    public Guid DoctorStaffMemberId { get; init; }

    public string DayOfWeek { get; init; } = string.Empty;

    public string StartLocalTime { get; init; } = string.Empty;

    public string EndLocalTime { get; init; } = string.Empty;

    public int SlotDurationMinutes { get; init; }

    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    public bool IsActive { get; init; }

    public int Version { get; init; }
}

public sealed class CreateDoctorAvailabilityRequest
{
    public string DayOfWeek { get; init; } = string.Empty;

    public string StartLocalTime { get; init; } = string.Empty;

    public string EndLocalTime { get; init; } = string.Empty;

    public int SlotDurationMinutes { get; init; } = 30;

    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }
}

public sealed class UpdateDoctorAvailabilityRequest
{
    public int ExpectedVersion { get; init; }

    public string? StartLocalTime { get; init; }

    public string? EndLocalTime { get; init; }

    public int? SlotDurationMinutes { get; init; }

    public DateOnly? EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }

    public bool? ClearEffectiveTo { get; init; }

    public bool? IsActive { get; init; }
}

public sealed class DoctorAvailabilityExceptionResponse
{
    public Guid Id { get; init; }

    public Guid DoctorStaffMemberId { get; init; }

    public DateOnly Date { get; init; }

    public string ExceptionType { get; init; } = string.Empty;

    public string? StartLocalTime { get; init; }

    public string? EndLocalTime { get; init; }

    public string? Reason { get; init; }

    public int Version { get; init; }
}

public sealed class CreateDoctorAvailabilityExceptionRequest
{
    public DateOnly Date { get; init; }

    public string ExceptionType { get; init; } = string.Empty;

    public string? StartLocalTime { get; init; }

    public string? EndLocalTime { get; init; }

    public string? Reason { get; init; }
}

public sealed class AvailableSlotResponse
{
    public DateTimeOffset StartUtc { get; init; }

    public DateTimeOffset EndUtc { get; init; }

    public string StartLocal { get; init; } = string.Empty;

    public string EndLocal { get; init; } = string.Empty;

    public int DurationMinutes { get; init; }

    public string TimeZoneId { get; init; } = string.Empty;
}

public sealed class AvailableSlotsQuery
{
    public DateOnly Date { get; init; }

    public int? DurationMinutes { get; init; }
}
