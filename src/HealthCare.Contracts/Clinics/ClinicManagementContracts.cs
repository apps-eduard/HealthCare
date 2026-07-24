namespace HealthCare.Contracts.Clinics;

public static class ClinicManagementErrorCodes
{
    public const string NotFound = "clinic.not_found";
    public const string AccessDenied = "clinic.access_denied";
    public const string InvalidScope = "clinic.invalid_scope";
    public const string OrganizationScopeRequired = "clinic.organization_scope_required";
    public const string InvalidSort = "clinic.invalid_sort";
    public const string NameRequired = "clinic.name_required";
    public const string SlugRequired = "clinic.slug_required";
    public const string SlugInvalid = "clinic.slug_invalid";
    public const string SlugInUse = "clinic.slug_in_use";
    public const string InvalidTimezone = "clinic.invalid_timezone";
    public const string InactiveOrganization = "clinic.inactive_organization";
    public const string AlreadyActive = "clinic.already_active";
    public const string AlreadyInactive = "clinic.already_inactive";
    public const string ActivationNotAllowed = "clinic.activation_not_allowed";
    public const string DeactivationNotAllowed = "clinic.deactivation_not_allowed";
    public const string ConcurrencyConflict = "clinic.concurrency_conflict";
    public const string LimitReached = "clinic.limit_reached";
    public const string InitialAdminFailed = "clinic.initial_admin_failed";
    public const string EmptyUpdate = "clinic.empty_update";
    public const string InvalidReason = "clinic.invalid_reason";
}

public sealed class OrganizationClinicSearchRequest
{
    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    /// <summary>PLATFORM_ADMIN explicit-bypass only.</summary>
    public Guid? OrganizationId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string SortBy { get; init; } = "name";

    public string SortDirection { get; init; } = "asc";
}

public sealed class OrganizationClinicListItemResponse
{
    public required Guid ClinicId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? Specialty { get; init; }

    public string? City { get; init; }

    public required string TimeZoneId { get; init; }

    public required bool IsActive { get; init; }

    public required int Version { get; init; }

    public int ActiveStaffCount { get; init; }

    public int ActiveDoctorCount { get; init; }
}

public sealed class OrganizationClinicDetailResponse
{
    public required Guid ClinicId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? Specialty { get; init; }

    public string? PhoneNumber { get; init; }

    public string? Email { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public string? PostalCode { get; init; }

    public string? Country { get; init; }

    public required string TimeZoneId { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public required int Version { get; init; }

    public int ActiveStaffCount { get; init; }

    public int ActiveDoctorCount { get; init; }

    public int? AppointmentCountToday { get; init; }
}

public sealed class CreateOrganizationClinicRequest
{
    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? Specialty { get; init; }

    public string? PhoneNumber { get; init; }

    public string? Email { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public string? PostalCode { get; init; }

    public string? Country { get; init; }

    public required string TimeZoneId { get; init; }

    /// <summary>PLATFORM_ADMIN explicit-bypass only.</summary>
    public Guid? OrganizationId { get; init; }

    public CreateClinicInitialAdminRequest? InitialClinicAdmin { get; init; }
}

public sealed class CreateClinicInitialAdminRequest
{
    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string TemporaryPassword { get; init; }

    public string? JobTitle { get; init; }
}

public sealed class UpdateOrganizationClinicRequest
{
    public string? Name { get; init; }

    public string? Slug { get; init; }

    public string? Specialty { get; init; }

    public string? PhoneNumber { get; init; }

    public string? Email { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public string? PostalCode { get; init; }

    public string? Country { get; init; }

    public string? TimeZoneId { get; init; }

    public required int ExpectedVersion { get; init; }
}

public sealed class ClinicActivationRequest
{
    public required int ExpectedVersion { get; init; }

    public string? Reason { get; init; }
}

public sealed class TimeZoneInfoResponse
{
    public required string TimeZoneId { get; init; }

    public required string DisplayName { get; init; }

    public required string UtcOffset { get; init; }
}
