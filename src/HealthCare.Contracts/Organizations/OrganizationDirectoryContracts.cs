namespace HealthCare.Contracts.Organizations;

public static class OrganizationErrorCodes
{
    public const string NotFound = "organization.not_found";
    public const string DirectoryAccessDenied = "organization.directory_access_denied";
    public const string ScopeRequired = "organization.scope_required";
    public const string Inactive = "organization.inactive";
    public const string InvalidSort = "organization.invalid_sort";
    public const string PlatformTenantContextRequired = "platform.tenant_context_required";
}

public sealed class OrganizationSearchRequest
{
    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string SortBy { get; init; } = "name";

    public string SortDirection { get; init; } = "asc";
}

public sealed class OrganizationDirectoryItemResponse
{
    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required bool IsActive { get; init; }

    public required int ClinicCount { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class OrganizationDetailResponse
{
    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required bool IsActive { get; init; }

    public required int ClinicCount { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
