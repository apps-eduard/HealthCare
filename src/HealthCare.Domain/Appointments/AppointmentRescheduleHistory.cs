namespace HealthCare.Domain.Appointments;

/// <summary>
/// Administrative reschedule audit row. No medical content or patient profile data.
/// </summary>
public sealed class AppointmentRescheduleHistory
{
    public Guid Id { get; set; }

    public Guid AppointmentId { get; set; }

    public Guid PreviousDoctorStaffMemberId { get; set; }

    public Guid NewDoctorStaffMemberId { get; set; }

    public DateTimeOffset PreviousStartUtc { get; set; }

    public DateTimeOffset NewStartUtc { get; set; }

    public int PreviousDurationMinutes { get; set; }

    public int NewDurationMinutes { get; set; }

    public Guid RescheduledByUserId { get; set; }

    public DateTimeOffset RescheduledAtUtc { get; set; }

    public string? Reason { get; set; }

    public int PreviousVersion { get; set; }
}
