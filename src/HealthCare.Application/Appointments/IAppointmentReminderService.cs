using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;

namespace HealthCare.Application.Appointments;

public interface IAppointmentReminderSender
{
    Task SendAsync(AppointmentReminderDeliveryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe delivery payload — no appointment reason, notes, or contact details.
/// </summary>
public sealed class AppointmentReminderDeliveryRequest
{
    public required Guid AppointmentId { get; init; }

    public required Guid ReminderId { get; init; }

    public required AppointmentReminderType ReminderType { get; init; }

    public required DateTimeOffset AppointmentDateUtc { get; init; }

    public required string AppointmentLocalDisplay { get; init; }

    public required string ClinicCode { get; init; }

    public required string TimeZoneId { get; init; }
}

public interface IReminderBackgroundJobs
{
    string EnqueueProcess(Guid appointmentId, Guid reminderId);

    string ScheduleProcess(Guid appointmentId, Guid reminderId, DateTimeOffset whenUtc);

    void TryDelete(string? jobId);
}

public interface IAppointmentReminderScheduler
{
    Task ScheduleAfterAppointmentCreatedAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    Task ScheduleAfterAppointmentConfirmedAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    Task ScheduleAfterAppointmentCancelledAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels/replaces pending Upcoming for the new schedule. Does not recreate Sent Confirmation.
    /// </summary>
    Task ScheduleAfterAppointmentRescheduledAsync(Guid appointmentId, CancellationToken cancellationToken = default);
}

public interface IAppointmentReminderProcessor
{
    Task ProcessReminderAsync(Guid appointmentId, Guid reminderId, CancellationToken cancellationToken = default);
}

public interface IAppointmentReminderRecoveryService
{
    Task RecoverOverdueAsync(CancellationToken cancellationToken = default);
}

public interface IAppointmentReminderService
{
    Task<IReadOnlyList<AppointmentReminderResponse>> ListForAppointmentAsync(
        Guid appointmentId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<AppointmentReminderResponse> RetryAsync(
        Guid appointmentId,
        Guid reminderId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class AppointmentReminderException : Exception
{
    public AppointmentReminderException(string errorCode, string title, int statusCode)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static AppointmentReminderException NotFound() =>
        new(AppointmentReminderErrorCodes.ReminderNotFound, "The reminder was not found.", 404);

    public static AppointmentReminderException AlreadySent() =>
        new(AppointmentReminderErrorCodes.ReminderAlreadySent, "The reminder was already sent.", 409);

    public static AppointmentReminderException NotRetryable() =>
        new(AppointmentReminderErrorCodes.ReminderNotRetryable, "The reminder cannot be retried.", 409);

    public static AppointmentReminderException DeliveryFailed() =>
        new(AppointmentReminderErrorCodes.ReminderDeliveryFailed, "Reminder delivery failed.", 409);
}
