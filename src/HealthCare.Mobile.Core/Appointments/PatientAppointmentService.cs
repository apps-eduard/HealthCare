using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Mobile.Core.Api;
using Microsoft.Extensions.Logging;

namespace HealthCare.Mobile.Core.Appointments;

public static class PatientAppointmentStatuses
{
    public const string Requested = "Requested";
    public const string Confirmed = "Confirmed";
    public const string CheckedIn = "CheckedIn";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string CancelledByPatient = "CancelledByPatient";
    public const string CancelledByClinic = "CancelledByClinic";
    public const string NoShow = "NoShow";

    /// <summary>Mirrors backend PatientScheduleMutationCutoff.</summary>
    public static readonly TimeSpan MutationCutoff = TimeSpan.FromHours(2);

    public static bool IsTerminal(string? status) =>
        status is Completed or CancelledByPatient or CancelledByClinic or NoShow;

    public static bool CanRescheduleStatus(string? status) =>
        status is Requested or Confirmed;

    public static bool CanCancelStatus(string? status) =>
        status is Requested or Confirmed;

    public static DateTimeOffset EndsAt(AppointmentResponse appointment) =>
        appointment.EndsAtUtc != default
            ? appointment.EndsAtUtc
            : appointment.AppointmentDateUtc.AddMinutes(appointment.DurationMinutes);

    public static bool IsUpcoming(AppointmentResponse appointment, DateTimeOffset utcNow) =>
        !IsTerminal(appointment.Status) && EndsAt(appointment) >= utcNow;

    public static bool IsPrevious(AppointmentResponse appointment, DateTimeOffset utcNow) =>
        !IsUpcoming(appointment, utcNow);

    public static bool IsOutsideMutationCutoff(AppointmentResponse appointment, DateTimeOffset utcNow) =>
        appointment.AppointmentDateUtc - utcNow >= MutationCutoff;

    public static bool CanCancel(AppointmentResponse appointment, DateTimeOffset utcNow) =>
        CanCancelStatus(appointment.Status) && IsOutsideMutationCutoff(appointment, utcNow);

    public static bool CanReschedule(AppointmentResponse appointment, DateTimeOffset utcNow) =>
        CanRescheduleStatus(appointment.Status) && IsOutsideMutationCutoff(appointment, utcNow);

    public static string DisplayStatus(string? status) =>
        status switch
        {
            Requested => "Requested",
            Confirmed => "Confirmed",
            CheckedIn => "Checked in",
            InProgress => "In progress",
            Completed => "Completed",
            CancelledByPatient => "Cancelled by you",
            CancelledByClinic => "Cancelled by clinic",
            NoShow => "No-show",
            _ => string.IsNullOrWhiteSpace(status) ? "Unknown" : status,
        };
}

public static class AppointmentTimeDisplay
{
    public static string FormatRange(AppointmentResponse appointment)
    {
        var end = PatientAppointmentStatuses.EndsAt(appointment);
        var tz = appointment.ClinicTimeZoneId;
        if (!string.IsNullOrWhiteSpace(tz))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(tz);
                var startLocal = TimeZoneInfo.ConvertTime(appointment.AppointmentDateUtc, zone);
                var endLocal = TimeZoneInfo.ConvertTime(end, zone);
                return $"{startLocal:yyyy-MM-dd} {startLocal:t} – {endLocal:t} ({tz})";
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall through to device-local.
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        var start = appointment.AppointmentDateUtc.ToLocalTime();
        var endDevice = end.ToLocalTime();
        return $"{start:yyyy-MM-dd} {start:t} – {endDevice:t} (device local)";
    }

    public static string FormatShort(AppointmentResponse appointment)
    {
        var tz = appointment.ClinicTimeZoneId;
        if (!string.IsNullOrWhiteSpace(tz))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(tz);
                var local = TimeZoneInfo.ConvertTime(appointment.AppointmentDateUtc, zone);
                return $"{local:yyyy-MM-dd} {local:t} ({tz})";
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        var device = appointment.AppointmentDateUtc.ToLocalTime();
        return $"{device:yyyy-MM-dd} {device:t} (device local)";
    }
}

public interface IPatientAppointmentService
{
    Task<ApiResult<PagedResponse<AppointmentResponse>>> ListAsync(
        AppointmentListQuery query,
        CancellationToken cancellationToken = default);

    Task<ApiResult<AppointmentResponse>> GetAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<ApiResult<AppointmentResponse>> CancelAsync(
        Guid appointmentId,
        int expectedVersion,
        string? cancellationReason = null,
        CancellationToken cancellationToken = default);

    Task<ApiResult<AppointmentResponse>> RescheduleAsync(
        Guid appointmentId,
        RescheduleAppointmentRequest request,
        CancellationToken cancellationToken = default);

    string MapMutationError(ApiProblem problem);
}

public sealed class PatientAppointmentService : IPatientAppointmentService
{
    private readonly IHealthCareApiClient _api;
    private readonly ILogger<PatientAppointmentService> _logger;

    public PatientAppointmentService(IHealthCareApiClient api, ILogger<PatientAppointmentService> logger)
    {
        _api = api;
        _logger = logger;
    }

    public Task<ApiResult<PagedResponse<AppointmentResponse>>> ListAsync(
        AppointmentListQuery query,
        CancellationToken cancellationToken = default) =>
        _api.ListPatientAppointmentsAsync(query, cancellationToken);

    public Task<ApiResult<AppointmentResponse>> GetAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default) =>
        _api.GetAppointmentAsync(appointmentId, cancellationToken);

    public Task<ApiResult<AppointmentResponse>> CancelAsync(
        Guid appointmentId,
        int expectedVersion,
        string? cancellationReason = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Patient appointment cancellation submitted.");
        return _api.CancelAppointmentAsync(
            appointmentId,
            new AppointmentActionRequest
            {
                ExpectedVersion = expectedVersion,
                CancellationReason = string.IsNullOrWhiteSpace(cancellationReason)
                    ? null
                    : cancellationReason.Trim(),
            },
            cancellationToken);
    }

    public Task<ApiResult<AppointmentResponse>> RescheduleAsync(
        Guid appointmentId,
        RescheduleAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Patient appointment reschedule submitted.");
        return _api.RescheduleAppointmentAsync(appointmentId, request, cancellationToken);
    }

    public string MapMutationError(ApiProblem problem) =>
        problem.ErrorCode switch
        {
            AppointmentErrorCodes.PatientMutationCutoff =>
                "Changes must be made at least two hours before the appointment. Please contact the clinic.",
            AppointmentErrorCodes.ConcurrencyConflict =>
                "This appointment changed. Reload the latest details before trying again.",
            AppointmentErrorCodes.SlotConflict =>
                "That time is no longer available. Choose another slot.",
            AppointmentErrorCodes.InvalidTransition =>
                "This appointment can no longer be changed.",
            AppointmentErrorCodes.RescheduleNotAllowed =>
                "This appointment cannot be rescheduled.",
            AppointmentErrorCodes.RescheduleSameSlot =>
                "Choose a different time than the current appointment.",
            AppointmentErrorCodes.NotFoundOrDenied =>
                "This appointment is not available.",
            _ => problem.UserMessage,
        };
}
