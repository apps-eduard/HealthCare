namespace HealthCare.Contracts.Security;

public static class OrganizationSecurityErrorCodes
{
    public const string AccessDenied = "organization_security.access_denied";
    public const string InvalidScope = "organization_security.invalid_scope";
    public const string OrganizationScopeRequired = "organization_security.organization_scope_required";
    public const string ClinicNotFound = "organization_security.clinic_not_found";
    public const string TargetNotFound = "organization_security.target_not_found";
    public const string InvalidDateRange = "organization_security.invalid_date_range";
    public const string OrganizationNotFound = "organization_security.organization_not_found";
    public const string PlatformAdminProtected = "organization_security.platform_admin_protected";
    public const string LastAdminProtected = "organization_security.last_admin_protected";
    public const string SelfCompromiseDenied = "organization_security.self_compromise_denied";
    public const string AlreadyInactive = "organization_security.already_inactive";
}

public sealed class OrganizationSecurityQuery
{
    public Guid? OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    public Guid? StaffMemberId { get; init; }

    public Guid? UserId { get; init; }

    public bool IncludeRevoked { get; init; }

    public string? FromUtc { get; init; }

    public string? ToUtc { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

public sealed class OrganizationSecuritySessionListResponse
{
    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public Guid? ClinicId { get; init; }

    public int ActiveSessionCount { get; init; }

    public int TotalCount { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public IReadOnlyList<OrganizationSecuritySessionItem> Items { get; init; } = [];
}

/// <summary>
/// Safe session visibility — never includes token hash or raw refresh tokens.
/// </summary>
public sealed class OrganizationSecuritySessionItem
{
    public Guid SessionId { get; init; }

    public Guid UserId { get; init; }

    public Guid? StaffMemberId { get; init; }

    public string? StaffDisplayName { get; init; }

    public string? StaffRole { get; init; }

    public Guid? ClinicId { get; init; }

    public string? ClinicName { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }

    public string? RevokedReason { get; init; }

    public bool IsActive { get; init; }

    public string? CreatedByIp { get; init; }

    public string? CreatedByUserAgent { get; init; }
}

public sealed class RevokeOrganizationSessionsRequest
{
    public string? Reason { get; init; }
}

public sealed class RevokeOrganizationSessionsResponse
{
    public required string Message { get; init; }

    public int RevokedRefreshTokenCount { get; init; }
}

public sealed class CompromisedAccountResponseRequest
{
    public int ExpectedVersion { get; init; }

    public string? Reason { get; init; }
}

public sealed class CompromisedAccountResponseResult
{
    public required string Message { get; init; }

    public Guid StaffMemberId { get; init; }

    public Guid UserId { get; init; }

    public int RevokedRefreshTokenCount { get; init; }
}

public sealed class OrganizationFailedLoginSummaryResponse
{
    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public int UsersWithFailedAttempts { get; init; }

    public int CurrentlyLockedOutUsers { get; init; }

    public int FailedLoginEventsInRange { get; init; }

    public IReadOnlyList<OrganizationFailedLoginUserItem> Users { get; init; } = [];
}

public sealed class OrganizationFailedLoginUserItem
{
    public Guid UserId { get; init; }

    public Guid? StaffMemberId { get; init; }

    public string? StaffDisplayName { get; init; }

    public Guid? ClinicId { get; init; }

    public string? ClinicName { get; init; }

    public int AccessFailedCount { get; init; }

    public DateTimeOffset? LockoutEndUtc { get; init; }

    public bool IsLockedOut { get; init; }

    public int RecentFailedLoginEvents { get; init; }
}

public sealed class OrganizationSecurityEventSummaryResponse
{
    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public string EventCategory { get; init; } = string.Empty;

    public int TotalCount { get; init; }

    public IReadOnlyList<OrganizationSecurityEventCountByOperation> ByOperation { get; init; } = [];

    public IReadOnlyList<OrganizationSecurityEventCountByClinic> ByClinic { get; init; } = [];

    public IReadOnlyList<OrganizationSecurityEventItem> RecentItems { get; init; } = [];
}

public sealed class OrganizationSecurityEventCountByOperation
{
    public required string Operation { get; init; }

    public required string ReasonCode { get; init; }

    public int Count { get; init; }
}

public sealed class OrganizationSecurityEventCountByClinic
{
    public Guid? ClinicId { get; init; }

    public string? ClinicName { get; init; }

    public int Count { get; init; }
}

public sealed class OrganizationSecurityEventItem
{
    public Guid EventId { get; init; }

    public required string EventType { get; init; }

    public required string Operation { get; init; }

    public required string ReasonCode { get; init; }

    public Guid? ClinicId { get; init; }

    public Guid? ActorUserId { get; init; }

    public Guid? TargetUserId { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }

    public string? CorrelationId { get; init; }
}
