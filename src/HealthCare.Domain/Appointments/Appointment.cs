namespace HealthCare.Domain.Appointments;

public enum AppointmentStatus
{
    Requested = 0,
    Confirmed = 1,
    CheckedIn = 2,
    InProgress = 3,
    Completed = 4,
    CancelledByPatient = 5,
    CancelledByClinic = 6,
    NoShow = 7,
}

public enum AppointmentSource
{
    Patient = 0,
    Staff = 1,
}

/// <summary>
/// Clinic-owned appointment. OrganizationId/ClinicId are server-owned; never trust client scope IDs.
/// </summary>
public sealed class Appointment
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClinicId { get; set; }

    public Guid PatientId { get; set; }

    public Guid ClinicPatientId { get; set; }

    public Guid DoctorStaffMemberId { get; set; }

    public DateTimeOffset AppointmentDateUtc { get; set; }

    public int DurationMinutes { get; set; }

    public string? Reason { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Requested;

    public string? PatientNotes { get; set; }

    public string? CancellationReason { get; set; }

    public AppointmentSource Source { get; set; }

    public Guid CreatedByUserId { get; set; }

    public int Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset EndsAtUtc => AppointmentDateUtc.AddMinutes(DurationMinutes);
}

/// <summary>
/// Minimal appointment status workflow. Invalid transitions throw; callers map to Problem Details.
/// </summary>
public static class AppointmentStatusTransitions
{
    private static readonly HashSet<(AppointmentStatus From, AppointmentStatus To)> Allowed =
    [
        (AppointmentStatus.Requested, AppointmentStatus.Confirmed),
        (AppointmentStatus.Requested, AppointmentStatus.CancelledByPatient),
        (AppointmentStatus.Requested, AppointmentStatus.CancelledByClinic),
        (AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn),
        (AppointmentStatus.Confirmed, AppointmentStatus.CancelledByPatient),
        (AppointmentStatus.Confirmed, AppointmentStatus.CancelledByClinic),
        (AppointmentStatus.Confirmed, AppointmentStatus.NoShow),
        (AppointmentStatus.CheckedIn, AppointmentStatus.InProgress),
        (AppointmentStatus.CheckedIn, AppointmentStatus.Completed),
        (AppointmentStatus.CheckedIn, AppointmentStatus.NoShow),
        (AppointmentStatus.CheckedIn, AppointmentStatus.CancelledByClinic),
        (AppointmentStatus.InProgress, AppointmentStatus.Completed),
        (AppointmentStatus.InProgress, AppointmentStatus.CancelledByClinic),
    ];

    public static bool IsTerminal(AppointmentStatus status) =>
        status is AppointmentStatus.Completed
            or AppointmentStatus.CancelledByPatient
            or AppointmentStatus.CancelledByClinic
            or AppointmentStatus.NoShow;

    public static bool IsActiveForScheduling(AppointmentStatus status) =>
        !IsCancelled(status);

    public static bool IsCancelled(AppointmentStatus status) =>
        status is AppointmentStatus.CancelledByPatient or AppointmentStatus.CancelledByClinic;

    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to) =>
        Allowed.Contains((from, to));

    public static void EnsureCanTransition(AppointmentStatus from, AppointmentStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid appointment status transition from {from} to {to}.");
        }
    }
}
