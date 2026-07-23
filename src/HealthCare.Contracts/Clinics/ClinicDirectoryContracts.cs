namespace HealthCare.Contracts.Clinics;

public static class ClinicErrorCodes
{
    public const string NotFound = "clinic.not_found";
    public const string DirectoryAccessDenied = "clinic.directory_access_denied";
    public const string InvalidScope = "clinic.invalid_scope";
    public const string OrganizationScopeRequired = "clinic.organization_scope_required";
    public const string InvalidSort = "clinic.invalid_sort";
}

public sealed class ClinicSearchRequest
{
    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    /// <summary>
    /// PLATFORM_ADMIN explicit-bypass only. Ignored for clinic/organization staff.
    /// </summary>
    public Guid? OrganizationId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string SortBy { get; init; } = "name";

    public string SortDirection { get; init; } = "asc";
}

public sealed class ClinicDirectoryItemResponse
{
    public required Guid ClinicId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required bool IsActive { get; init; }

    public required string TimeZoneId { get; init; }

    public string? City { get; init; }
}

public sealed class ClinicDetailResponse
{
    public required Guid ClinicId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required bool IsActive { get; init; }

    public required string TimeZoneId { get; init; }

    public string? Specialty { get; init; }

    public string? Address { get; init; }

    public string? City { get; init; }

    public string? PhoneNumber { get; init; }
}
