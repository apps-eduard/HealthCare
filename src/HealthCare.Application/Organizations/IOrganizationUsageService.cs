using HealthCare.Application.Authorization;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public interface IOrganizationUsageService
{
    Task<OrganizationUsageResponse> GetUsageAsync(
        OrganizationUsageQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationUsageException : Exception
{
    public OrganizationUsageException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static OrganizationUsageException AccessDenied() =>
        new(OrganizationUsageErrorCodes.AccessDenied, "Organization usage access is denied.", 403);

    public static OrganizationUsageException InvalidScope() =>
        new(OrganizationUsageErrorCodes.InvalidScope, "The requested usage scope is invalid.", 400);

    public static OrganizationUsageException OrganizationScopeRequired() =>
        new(OrganizationUsageErrorCodes.OrganizationScopeRequired, "An organization scope is required.", 400);

    public static OrganizationUsageException ClinicNotFound() =>
        new(OrganizationUsageErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static OrganizationUsageException OrganizationNotFound() =>
        new(OrganizationUsageErrorCodes.OrganizationNotFound, "Organization was not found.", 404);
}
