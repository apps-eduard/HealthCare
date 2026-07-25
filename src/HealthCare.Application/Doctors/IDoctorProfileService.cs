using HealthCare.Application.Authorization;
using HealthCare.Contracts.Doctors;

namespace HealthCare.Application.Doctors;

public interface IDoctorProfileService
{
    Task<DoctorProfileResponse> GetAsync(
        DoctorProfileQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<DoctorProfileResponse> UpdateAsync(
        UpdateDoctorProfileRequest request,
        DoctorProfileQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorProfileException : Exception
{
    public DoctorProfileException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static DoctorProfileException AccessDenied() =>
        new(DoctorProfileErrorCodes.AccessDenied, "Doctor profile access is denied.", 403);

    public static DoctorProfileException InvalidScope() =>
        new(DoctorProfileErrorCodes.InvalidScope, "The requested doctor profile scope is invalid.", 400);

    public static DoctorProfileException ClinicScopeRequired() =>
        new(
            DoctorProfileErrorCodes.ClinicScopeRequired,
            "An explicit clinic scope is required.",
            400);

    public static DoctorProfileException DoctorScopeRequired() =>
        new(
            DoctorProfileErrorCodes.DoctorScopeRequired,
            "An explicit doctor staff member scope is required.",
            400);

    public static DoctorProfileException ClinicNotFound() =>
        new(DoctorProfileErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static DoctorProfileException DoctorNotFound() =>
        new(DoctorProfileErrorCodes.DoctorNotFound, "Doctor was not found.", 404);

    public static DoctorProfileException EmptyUpdate() =>
        new(DoctorProfileErrorCodes.EmptyUpdate, "No doctor profile fields were provided to update.", 400);

    public static DoctorProfileException InvalidField(string title) =>
        new(DoctorProfileErrorCodes.InvalidField, title, 400);

    public static DoctorProfileException ConcurrencyConflict() =>
        new(
            DoctorProfileErrorCodes.ConcurrencyConflict,
            "Doctor profile was modified by another request. Reload and retry.",
            409);
}
