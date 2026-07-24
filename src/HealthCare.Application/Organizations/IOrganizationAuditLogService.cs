using HealthCare.Application.Authorization;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public interface IOrganizationAuditLogService
{
    Task<OrganizationAuditLogListResponse> SearchAsync(
        OrganizationAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        OrganizationAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationAuditLogListResponse> GetByCorrelationIdAsync(
        string correlationId,
        OrganizationAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationAuditLogException : Exception
{
    public OrganizationAuditLogException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static OrganizationAuditLogException AccessDenied() =>
        new(OrganizationAuditLogErrorCodes.AccessDenied, "Organization audit log access is denied.", 403);

    public static OrganizationAuditLogException InvalidScope() =>
        new(OrganizationAuditLogErrorCodes.InvalidScope, "The requested audit log scope is invalid.", 400);

    public static OrganizationAuditLogException OrganizationScopeRequired() =>
        new(OrganizationAuditLogErrorCodes.OrganizationScopeRequired, "An organization scope is required.", 400);

    public static OrganizationAuditLogException ClinicNotFound() =>
        new(OrganizationAuditLogErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static OrganizationAuditLogException NotFound() =>
        new(OrganizationAuditLogErrorCodes.NotFound, "Audit event was not found.", 404);

    public static OrganizationAuditLogException InvalidDateRange() =>
        new(OrganizationAuditLogErrorCodes.InvalidDateRange, "The audit log date range is invalid.", 400);

    public static OrganizationAuditLogException OrganizationNotFound() =>
        new(OrganizationAuditLogErrorCodes.OrganizationNotFound, "Organization was not found.", 404);
}
