using HealthCare.Application.Authorization;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public interface IClinicAuditLogService
{
    Task<ClinicAuditLogListResponse> SearchAsync(
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicAuditLogListResponse> GetByCorrelationIdAsync(
        string correlationId,
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicAuditLogException : Exception
{
    public ClinicAuditLogException(string errorCode, string title, int statusCode = 403)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static ClinicAuditLogException AccessDenied() =>
        new(ClinicAuditLogErrorCodes.AccessDenied, "Clinic audit log access is denied.", 403);

    public static ClinicAuditLogException InvalidScope() =>
        new(ClinicAuditLogErrorCodes.InvalidScope, "The requested clinic audit scope is invalid.", 400);

    public static ClinicAuditLogException ClinicScopeRequired() =>
        new(ClinicAuditLogErrorCodes.ClinicScopeRequired, "A clinic scope is required.", 400);

    public static ClinicAuditLogException ClinicNotFound() =>
        new(ClinicAuditLogErrorCodes.ClinicNotFound, "Clinic was not found.", 404);

    public static ClinicAuditLogException NotFound() =>
        new(ClinicAuditLogErrorCodes.NotFound, "The audit event was not found.", 404);

    public static ClinicAuditLogException InvalidDateRange() =>
        new(ClinicAuditLogErrorCodes.InvalidDateRange, "The audit date range is invalid.", 400);
}

/// <summary>
/// Explicit allowlist of operational audit actions visible to Clinic Admin.
/// Unknown / future actions are excluded by default (allowlist, not blacklist).
/// </summary>
public static class ClinicAuditActionAllowlist
{
    public static readonly IReadOnlySet<string> Actions =
        new HashSet<string>(ClinicAuditActions.All, StringComparer.Ordinal);

    public static readonly string[] ActionValues = ClinicAuditActions.All;

    public static bool IsAllowed(string? action) =>
        !string.IsNullOrWhiteSpace(action) && Actions.Contains(action.Trim());

    public static string ToSummary(string action) => ClinicAuditActions.ToSummary(action);
}
