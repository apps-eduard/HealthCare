using HealthCare.Application.Authorization;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public interface IClinicSettingsService
{
    Task<ClinicSettingsResponse> GetAsync(
        ClinicSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicSettingsResponse> UpdateAsync(
        UpdateClinicSettingsRequest request,
        ClinicSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicSettingsException : Exception
{
    public ClinicSettingsException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static ClinicSettingsException AccessDenied() =>
        new(ClinicSettingsErrorCodes.AccessDenied, "Clinic settings access is denied.", 403);

    public static ClinicSettingsException InvalidScope() =>
        new(ClinicSettingsErrorCodes.InvalidScope, "The requested clinic settings scope is invalid.", 400);

    public static ClinicSettingsException ClinicScopeRequired() =>
        new(
            ClinicSettingsErrorCodes.ClinicScopeRequired,
            "An explicit clinic scope is required.",
            400);

    public static ClinicSettingsException ClinicNotFound() =>
        new(ClinicSettingsErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static ClinicSettingsException EmptyUpdate() =>
        new(ClinicSettingsErrorCodes.EmptyUpdate, "No clinic profile fields were provided to update.", 400);

    public static ClinicSettingsException InvalidTimezone() =>
        new(ClinicSettingsErrorCodes.InvalidTimezone, "Clinic default timezone is invalid.", 400);

    public static ClinicSettingsException ConcurrencyConflict() =>
        new(
            ClinicSettingsErrorCodes.ConcurrencyConflict,
            "Clinic was modified by another request. Reload and retry.",
            409);
}
