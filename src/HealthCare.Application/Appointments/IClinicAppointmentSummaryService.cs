using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;

namespace HealthCare.Application.Appointments;

public interface IClinicAppointmentSummarySender
{
    Task SendAsync(ClinicAppointmentSummaryResponse summary, CancellationToken cancellationToken = default);
}

public interface IClinicAppointmentSummaryBuilder
{
    Task<ClinicAppointmentSummaryResponse> BuildAsync(
        Guid clinicId,
        DateOnly summaryDate,
        CancellationToken cancellationToken = default);
}

public interface IClinicAppointmentSummaryDispatcher
{
    /// <summary>
    /// Scans active clinics and enqueues due daily summary runs (06:00 clinic-local, same-day coverage).
    /// </summary>
    Task DispatchDueAsync(CancellationToken cancellationToken = default);
}

public interface IClinicAppointmentSummaryProcessor
{
    Task ProcessRunAsync(Guid runId, CancellationToken cancellationToken = default);
}

public interface IClinicAppointmentSummaryRecoveryService
{
    Task RecoverAsync(CancellationToken cancellationToken = default);
}

public interface IClinicAppointmentSummaryJobs
{
    string EnqueueProcess(Guid runId);

    void TryDelete(string? jobId);
}

public interface IClinicAppointmentSummaryService
{
    Task<ClinicAppointmentSummaryResponse> GetForStaffAsync(
        ClinicAppointmentSummaryQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<ClinicAppointmentSummaryRunResponse>> ListRunsForStaffAsync(
        ClinicAppointmentSummaryRunQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicAppointmentSummaryRunResponse> RetryAsync(
        Guid clinicId,
        DateOnly summaryDate,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public interface IStaffOperationsHealthService
{
    Task<StaffOperationsHealthResponse> GetHealthAsync(
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class AppointmentSummaryException : Exception
{
    public AppointmentSummaryException(string errorCode, string title, int statusCode)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static AppointmentSummaryException NotFound() =>
        new(AppointmentSummaryErrorCodes.SummaryNotFound, "The appointment summary was not found.", 404);

    public static AppointmentSummaryException AlreadyCompleted() =>
        new(AppointmentSummaryErrorCodes.SummaryAlreadyCompleted, "The appointment summary was already completed.", 409);

    public static AppointmentSummaryException NotRetryable() =>
        new(AppointmentSummaryErrorCodes.SummaryNotRetryable, "The appointment summary cannot be retried.", 409);

    public static AppointmentSummaryException GenerationFailed() =>
        new(AppointmentSummaryErrorCodes.SummaryGenerationFailed, "Appointment summary generation failed.", 409);

    public static AppointmentSummaryException DeliveryFailed() =>
        new(AppointmentSummaryErrorCodes.SummaryDeliveryFailed, "Appointment summary delivery failed.", 409);

    public static AppointmentSummaryException InvalidDate() =>
        new(AppointmentSummaryErrorCodes.SummaryInvalidDate, "The summary date is invalid.", 400);
}
