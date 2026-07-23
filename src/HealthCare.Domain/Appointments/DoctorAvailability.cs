namespace HealthCare.Domain.Appointments;

public enum AvailabilityExceptionType
{
    UnavailableFullDay = 0,
    UnavailableRange = 1,
    CustomAvailableRange = 2,
}

/// <summary>
/// Recurring weekly availability window for a doctor in one clinic.
/// Times are clinic-local; Appointment.AppointmentDateUtc remains UTC.
/// </summary>
public sealed class DoctorAvailability
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClinicId { get; set; }

    public Guid DoctorStaffMemberId { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartLocalTime { get; set; }

    public TimeOnly EndLocalTime { get; set; }

    public int SlotDurationMinutes { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    public DateOnly? EffectiveTo { get; set; }

    public bool IsActive { get; set; } = true;

    public int Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// Date-specific availability override for a doctor in one clinic.
/// </summary>
public sealed class DoctorAvailabilityException
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClinicId { get; set; }

    public Guid DoctorStaffMemberId { get; set; }

    public DateOnly Date { get; set; }

    public AvailabilityExceptionType ExceptionType { get; set; }

    public TimeOnly? StartLocalTime { get; set; }

    public TimeOnly? EndLocalTime { get; set; }

    public string? Reason { get; set; }

    public int Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// Pure slot-boundary and window helpers (no I/O).
/// </summary>
public static class AvailabilitySlotRules
{
    public const int MinSlotDurationMinutes = 10;
    public const int MaxSlotDurationMinutes = 240;
    public const string DefaultTimeZoneId = "Asia/Riyadh";

    public static bool IsValidDuration(int minutes) =>
        minutes is >= MinSlotDurationMinutes and <= MaxSlotDurationMinutes;

    public static bool IsOnSlotBoundary(TimeOnly start, TimeOnly windowStart, int slotDurationMinutes)
    {
        if (slotDurationMinutes <= 0 || start < windowStart)
        {
            return false;
        }

        var delta = (int)(start - windowStart).TotalMinutes;
        return delta % slotDurationMinutes == 0;
    }

    public static bool FitsWithinWindow(TimeOnly start, int durationMinutes, TimeOnly windowEnd)
    {
        var endMinutes = start.Hour * 60 + start.Minute + durationMinutes;
        var windowEndMinutes = windowEnd.Hour * 60 + windowEnd.Minute;
        return endMinutes <= windowEndMinutes;
    }

    public static IReadOnlyList<TimeOnly> GenerateSlotStarts(
        TimeOnly windowStart,
        TimeOnly windowEnd,
        int slotDurationMinutes)
    {
        if (!IsValidDuration(slotDurationMinutes) || windowStart >= windowEnd)
        {
            return Array.Empty<TimeOnly>();
        }

        var starts = new List<TimeOnly>();
        var cursor = windowStart;
        while (FitsWithinWindow(cursor, slotDurationMinutes, windowEnd))
        {
            starts.Add(cursor);
            var next = cursor.AddMinutes(slotDurationMinutes);
            if (next <= cursor)
            {
                break;
            }

            cursor = next;
        }

        return starts;
    }

    public static bool TimeRangesOverlap(TimeOnly aStart, TimeOnly aEnd, TimeOnly bStart, TimeOnly bEnd) =>
        aStart < bEnd && bStart < aEnd;

    public static bool DateRangesOverlap(DateOnly aFrom, DateOnly? aTo, DateOnly bFrom, DateOnly? bTo)
    {
        var aEnd = aTo ?? DateOnly.MaxValue;
        var bEnd = bTo ?? DateOnly.MaxValue;
        return aFrom <= bEnd && bFrom <= aEnd;
    }
}
