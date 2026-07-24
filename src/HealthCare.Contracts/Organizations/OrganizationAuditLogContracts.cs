namespace HealthCare.Contracts.Organizations;

public static class OrganizationAuditLogErrorCodes
{
    public const string AccessDenied = "organization_audit.access_denied";
    public const string InvalidScope = "organization_audit.invalid_scope";
    public const string OrganizationScopeRequired = "organization_audit.organization_scope_required";
    public const string ClinicNotFound = "organization_audit.clinic_not_found";
    public const string NotFound = "organization_audit.not_found";
    public const string InvalidDateRange = "organization_audit.invalid_date_range";
    public const string OrganizationNotFound = "organization_audit.organization_not_found";
}

public sealed class OrganizationAuditLogQuery
{
    public Guid? OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    public Guid? ActorUserId { get; init; }

    public string? Category { get; init; }

    public string? Action { get; init; }

    public string? ResultCode { get; init; }

    public string? CorrelationId { get; init; }

    public string? FromUtc { get; init; }

    public string? ToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed class OrganizationAuditLogListResponse
{
    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public Guid? ClinicId { get; init; }

    public int RetentionDays { get; init; }

    public int TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public IReadOnlyList<OrganizationAuditLogItem> Items { get; init; } = [];
}

/// <summary>
/// Safe audit list/detail item — no passwords, tokens, PHI, or request bodies.
/// </summary>
public sealed class OrganizationAuditLogItem
{
    public Guid Id { get; init; }

    public Guid OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    public string? ClinicName { get; init; }

    public Guid? ActorUserId { get; init; }

    public required string Category { get; init; }

    public required string Action { get; init; }

    public required string ResultCode { get; init; }

    public string? ResourceType { get; init; }

    public Guid? ResourceId { get; init; }

    public string? CorrelationId { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }
}

public sealed class OrganizationAuditLogDetailResponse
{
    public required OrganizationAuditLogItem Event { get; init; }

    public int RetentionDays { get; init; }
}
