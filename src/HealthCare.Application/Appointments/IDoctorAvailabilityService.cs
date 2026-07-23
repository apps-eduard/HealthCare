using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;

namespace HealthCare.Application.Appointments;

public interface IClinicTimeZoneConverter
{
    TimeZoneInfo Resolve(string timeZoneId);

    DateTimeOffset ToUtc(DateOnly date, TimeOnly localTime, string timeZoneId);

    DateTimeOffset ToClinicLocal(DateTimeOffset utc, string timeZoneId);

    DateOnly GetClinicDate(DateTimeOffset utc, string timeZoneId);

    TimeOnly GetClinicTime(DateTimeOffset utc, string timeZoneId);
}

public interface IDoctorDirectoryService
{
    Task<IReadOnlyList<ClinicDoctorResponse>> ListDoctorsByClinicCodeAsync(
        string clinicCode,
        CancellationToken cancellationToken = default);
}

public interface IDoctorAvailabilityService
{
    Task<IReadOnlyList<DoctorAvailabilityResponse>> ListAvailabilityAsync(
        Guid staffMemberId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorAvailabilityExceptionResponse>> ListExceptionsAsync(
        Guid staffMemberId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<DoctorAvailabilityResponse> CreateAvailabilityAsync(
        Guid staffMemberId,
        CreateDoctorAvailabilityRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<DoctorAvailabilityResponse> UpdateAvailabilityAsync(
        Guid staffMemberId,
        Guid availabilityId,
        UpdateDoctorAvailabilityRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task DeleteAvailabilityAsync(
        Guid staffMemberId,
        Guid availabilityId,
        int expectedVersion,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<DoctorAvailabilityExceptionResponse> CreateExceptionAsync(
        Guid staffMemberId,
        CreateDoctorAvailabilityExceptionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task DeleteExceptionAsync(
        Guid staffMemberId,
        Guid exceptionId,
        int expectedVersion,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public interface IAppointmentSlotService
{
    Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(
        string clinicCode,
        Guid staffMemberId,
        AvailableSlotsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a proposed UTC appointment falls in an available slot for the doctor/clinic.
    /// </summary>
    Task EnsureSlotIsBookableAsync(
        Guid clinicId,
        Guid doctorStaffMemberId,
        DateTimeOffset startUtc,
        int durationMinutes,
        Guid? excludeAppointmentId = null,
        CancellationToken cancellationToken = default);
}

public sealed class AvailabilityException : Exception
{
    public AvailabilityException(string errorCode, string title, int statusCode)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static AvailabilityException OutsideAvailability() =>
        new(AvailabilityErrorCodes.OutsideAvailability, "The requested time is outside doctor availability.", 409);

    public static AvailabilityException SlotUnavailable() =>
        new(AvailabilityErrorCodes.SlotUnavailable, "The requested appointment slot is unavailable.", 409);

    public static AvailabilityException BlockedByException() =>
        new(AvailabilityErrorCodes.AvailabilityException, "The requested time is blocked by an availability exception.", 409);

    public static AvailabilityException InvalidSlotDuration() =>
        new(AvailabilityErrorCodes.InvalidSlotDuration, "The appointment duration is invalid for this availability.", 409);

    public static AvailabilityException Invalid() =>
        new(AvailabilityErrorCodes.InvalidAvailability, "The availability definition is invalid.", 400);

    public static AvailabilityException Conflict() =>
        new(AvailabilityErrorCodes.AvailabilityConflict, "The availability window conflicts with an existing window.", 409);

    public static AvailabilityException Concurrency() =>
        new(AvailabilityErrorCodes.AvailabilityConcurrency, "The availability record was modified by another request.", 409);

    public static AvailabilityException InvalidTimeZone() =>
        new(AvailabilityErrorCodes.InvalidTimeZone, "The clinic timezone is invalid.", 400);

    public static AvailabilityException DoctorNotFound() =>
        new(AvailabilityErrorCodes.DoctorNotFound, "The doctor was not found.", 404);
}
