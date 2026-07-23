namespace HealthCare.Contracts.Staff;

public sealed class StaffSearchRequest
{
    public string? Search { get; init; }

    public Guid? ClinicId { get; init; }

    public string? Role { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public string SortBy { get; init; } = "lastname";

    public string SortDirection { get; init; } = "asc";
}

public sealed class StaffSummaryResponse
{
    public required Guid StaffMemberId { get; init; }

    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? DisplayName { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid ClinicId { get; init; }

    public required string Role { get; init; }

    public required bool MembershipIsActive { get; init; }

    public required bool AccountIsActive { get; init; }

    public required int Version { get; init; }
}

public sealed class StaffDetailResponse
{
    public required Guid StaffMemberId { get; init; }

    public required Guid UserId { get; init; }

    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? DisplayName { get; init; }

    public string? JobTitle { get; init; }

    public string? PhoneNumber { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid ClinicId { get; init; }

    public required string Role { get; init; }

    public required bool MembershipIsActive { get; init; }

    public required bool AccountIsActive { get; init; }

    public required bool EmailConfirmed { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public required int Version { get; init; }
}

public sealed class CreateStaffRequest
{
    public required string Email { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? DisplayName { get; init; }

    public string? JobTitle { get; init; }

    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Required for organization admins and platform admins; ignored for clinic admins (trusted clinic used).
    /// </summary>
    public Guid? ClinicId { get; init; }

    public required string Role { get; init; }

    /// <summary>
    /// Temporary password for the temporary-password creation workflow (change-on-login deferred).
    /// </summary>
    public required string TemporaryPassword { get; init; }
}

public sealed class CreateStaffResponse
{
    public required StaffDetailResponse Staff { get; init; }
}

public sealed class UpdateStaffRequest
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? DisplayName { get; init; }

    public string? JobTitle { get; init; }

    public string? PhoneNumber { get; init; }

    public required int ExpectedVersion { get; init; }
}

public sealed class StaffActivationRequest
{
    public required int ExpectedVersion { get; init; }

    public string? Reason { get; init; }
}

public sealed class StaffRoleInfoResponse
{
    public required string Name { get; init; }

    public required string DisplayLabel { get; init; }

    public required bool AssignableByCurrentUser { get; init; }
}
