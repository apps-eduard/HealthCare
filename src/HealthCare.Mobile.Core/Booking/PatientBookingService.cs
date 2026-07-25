using HealthCare.Contracts.Appointments;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Discovery;
using Microsoft.Extensions.Logging;

namespace HealthCare.Mobile.Core.Booking;

/// <summary>
/// Patient-facing booking receipt after a confirmed successful create.
/// Does not trigger another API call on navigation/refresh.
/// </summary>
public sealed record BookingReceipt
{
    public string Status { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string? ClinicName { get; init; }

    public string? ClinicCode { get; init; }

    public string? DoctorDisplayName { get; init; }

    public DateTimeOffset AppointmentDateUtc { get; init; }

    public int DurationMinutes { get; init; }

    public string? ClinicTimeZoneId { get; init; }

    public string? Reason { get; init; }

    public AvailableSlotResponse? SlotSnapshot { get; init; }
}

public interface IBookingReceiptStore
{
    BookingReceipt? LastSuccess { get; }

    void Set(BookingReceipt receipt);

    void Clear();
}

public sealed class BookingReceiptStore : IBookingReceiptStore
{
    private readonly object _gate = new();
    private BookingReceipt? _last;

    public BookingReceipt? LastSuccess
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    public void Set(BookingReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        lock (_gate)
        {
            _last = receipt;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _last = null;
        }
    }
}

/// <summary>Mirrors Application CreatePatientAppointmentRequestValidator limits.</summary>
public static class PatientBookingLimits
{
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 480;
    public const int MaxReasonLength = 500;
}

public interface IPatientBookingService
{
    bool IsSelectionReady(DiscoverySelection selection);

    string? ValidateReason(string? reason);

    CreatePatientAppointmentRequest BuildRequest(DiscoverySelection selection, string? reason);

    Task<ApiResult<AppointmentResponse>> SubmitAsync(
        CreatePatientAppointmentRequest request,
        CancellationToken cancellationToken = default);

    BookingReceipt ToReceipt(AppointmentResponse response, DiscoverySelection selection, AvailableSlotResponse? slot);

    string MapConflictMessage(ApiProblem problem);
}

public sealed class PatientBookingService : IPatientBookingService
{
    private readonly IHealthCareApiClient _api;
    private readonly ILogger<PatientBookingService> _logger;

    public PatientBookingService(IHealthCareApiClient api, ILogger<PatientBookingService> logger)
    {
        _api = api;
        _logger = logger;
    }

    public bool IsSelectionReady(DiscoverySelection selection)
    {
        if (string.IsNullOrWhiteSpace(selection.ClinicCode))
        {
            return false;
        }

        if (selection.DoctorStaffMemberId is null || selection.DoctorStaffMemberId == Guid.Empty)
        {
            return false;
        }

        if (selection.SelectedSlot is null)
        {
            return false;
        }

        if (selection.SelectedSlot.StartUtc == default || selection.SelectedSlot.EndUtc == default)
        {
            return false;
        }

        return selection.SelectedSlot.EndUtc > selection.SelectedSlot.StartUtc;
    }

    public string? ValidateReason(string? reason)
    {
        if (reason is null)
        {
            return null;
        }

        if (reason.Length > PatientBookingLimits.MaxReasonLength)
        {
            return $"Reason must be at most {PatientBookingLimits.MaxReasonLength} characters.";
        }

        return null;
    }

    public CreatePatientAppointmentRequest BuildRequest(DiscoverySelection selection, string? reason)
    {
        if (!IsSelectionReady(selection))
        {
            throw new InvalidOperationException("Discovery selection is incomplete.");
        }

        var slot = selection.SelectedSlot!;
        var duration = slot.DurationMinutes > 0
            ? slot.DurationMinutes
            : (int)Math.Round((slot.EndUtc - slot.StartUtc).TotalMinutes);

        duration = Math.Clamp(
            duration,
            PatientBookingLimits.MinDurationMinutes,
            PatientBookingLimits.MaxDurationMinutes);

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is { Length: > PatientBookingLimits.MaxReasonLength })
        {
            trimmedReason = trimmedReason[..PatientBookingLimits.MaxReasonLength];
        }

        return new CreatePatientAppointmentRequest
        {
            ClinicCode = selection.ClinicCode!.Trim().ToLowerInvariant(),
            DoctorStaffMemberId = selection.DoctorStaffMemberId!.Value,
            AppointmentDateUtc = slot.StartUtc,
            DurationMinutes = duration,
            Reason = trimmedReason,
        };
    }

    public Task<ApiResult<AppointmentResponse>> SubmitAsync(
        CreatePatientAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        // Intentionally no application-level retry. Auth 401 refresh is handled by the HTTP pipeline only.
        _logger.LogInformation("Patient appointment booking submitted.");
        return _api.CreatePatientAppointmentAsync(request, cancellationToken);
    }

    public BookingReceipt ToReceipt(
        AppointmentResponse response,
        DiscoverySelection selection,
        AvailableSlotResponse? slot) =>
        new()
        {
            Status = response.Status,
            Source = response.Source,
            ClinicName = response.ClinicName ?? selection.ClinicName,
            ClinicCode = response.ClinicSlug ?? selection.ClinicCode,
            DoctorDisplayName = response.DoctorDisplayName ?? selection.DoctorDisplayName,
            AppointmentDateUtc = response.AppointmentDateUtc,
            DurationMinutes = response.DurationMinutes,
            ClinicTimeZoneId = response.ClinicTimeZoneId ?? slot?.TimeZoneId,
            Reason = response.Reason,
            SlotSnapshot = slot,
        };

    public string MapConflictMessage(ApiProblem problem)
    {
        return problem.ErrorCode switch
        {
            AppointmentErrorCodes.SlotConflict =>
                "That time is no longer available. Choose another slot.",
            AppointmentErrorCodes.ConcurrencyConflict =>
                "Your selection is out of date. Choose a slot again.",
            AppointmentErrorCodes.NotEnrolled =>
                "You must enroll with this clinic before booking.",
            AppointmentErrorCodes.InactiveClinic =>
                "This clinic is not available for booking.",
            AppointmentErrorCodes.InactivePatient =>
                "Your Patient account cannot book appointments right now.",
            AppointmentErrorCodes.InvalidAssignedStaff =>
                "That Doctor is not available for booking.",
            AppointmentErrorCodes.InvalidTime =>
                "The selected time is no longer valid. Choose another slot.",
            _ => problem.UserMessage,
        };
    }
}
