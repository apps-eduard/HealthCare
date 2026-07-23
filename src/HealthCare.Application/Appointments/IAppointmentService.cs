using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;

namespace HealthCare.Application.Appointments;

public interface IAppointmentService
{
    Task<AppointmentResponse> CreateForCurrentPatientAsync(
        CreatePatientAppointmentRequest request,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CreateForStaffAsync(
        CreateStaffAppointmentRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<AppointmentResponse>> ListForCurrentPatientAsync(
        AppointmentListQuery query,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<AppointmentResponse>> ListForStaffAsync(
        AppointmentListQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> GetByIdAsync(
        Guid appointmentId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> ConfirmAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CancelAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CheckInAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CompleteAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> MarkNoShowAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class AppointmentException : Exception
{
    public AppointmentException(string errorCode, string title, int statusCode)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static AppointmentException NotEnrolled() =>
        new(AppointmentErrorCodes.NotEnrolled, "Patient is not enrolled in the clinic.", 403);

    public static AppointmentException InactivePatient() =>
        new(AppointmentErrorCodes.InactivePatient, "The patient account is not active.", 400);

    public static AppointmentException InactiveClinic() =>
        new(AppointmentErrorCodes.InactiveClinic, "The clinic is not available.", 400);

    public static AppointmentException InvalidAssignedStaff() =>
        new(AppointmentErrorCodes.InvalidAssignedStaff, "The assigned staff member is invalid for this clinic.", 400);

    public static AppointmentException NotFoundOrDenied() =>
        new(AppointmentErrorCodes.NotFoundOrDenied, "Appointment was not found.", 404);

    public static AppointmentException InvalidTransition() =>
        new(AppointmentErrorCodes.InvalidTransition, "The appointment status transition is not allowed.", 409);

    public static AppointmentException SlotConflict() =>
        new(AppointmentErrorCodes.SlotConflict, "The appointment slot conflicts with an existing booking.", 409);

    public static AppointmentException ConcurrencyConflict() =>
        new(AppointmentErrorCodes.ConcurrencyConflict, "The appointment was modified by another request. Reload and retry.", 409);

    public static AppointmentException InvalidTime() =>
        new(AppointmentErrorCodes.InvalidTime, "The appointment time is invalid.", 400);
}
