using HealthCare.Application.Authorization;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public interface IOrganizationSettingsService
{
    Task<OrganizationSettingsResponse> GetAsync(
        OrganizationSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationSettingsResponse> UpdateAsync(
        UpdateOrganizationSettingsRequest request,
        OrganizationSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationSettingsException : Exception
{
    public OrganizationSettingsException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static OrganizationSettingsException AccessDenied() =>
        new(OrganizationSettingsErrorCodes.AccessDenied, "Organization settings access is denied.", 403);

    public static OrganizationSettingsException InvalidScope() =>
        new(OrganizationSettingsErrorCodes.InvalidScope, "The requested organization settings scope is invalid.", 400);

    public static OrganizationSettingsException OrganizationScopeRequired() =>
        new(
            OrganizationSettingsErrorCodes.OrganizationScopeRequired,
            "An organization scope is required.",
            400);

    public static OrganizationSettingsException OrganizationNotFound() =>
        new(OrganizationSettingsErrorCodes.OrganizationNotFound, "Organization was not found.", 404);

    public static OrganizationSettingsException EmptyUpdate() =>
        new(OrganizationSettingsErrorCodes.EmptyUpdate, "No organization profile fields were provided to update.", 400);

    public static OrganizationSettingsException InvalidTimezone() =>
        new(OrganizationSettingsErrorCodes.InvalidTimezone, "Organization default timezone is invalid.", 400);

    public static OrganizationSettingsException ConcurrencyConflict() =>
        new(
            OrganizationSettingsErrorCodes.ConcurrencyConflict,
            "Organization was modified by another request. Reload and retry.",
            409);
}
