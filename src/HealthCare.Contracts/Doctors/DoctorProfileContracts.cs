using System.Text.Json.Serialization;

namespace HealthCare.Contracts.Doctors;

public static class DoctorProfileErrorCodes
{
    public const string AccessDenied = "doctor_profile.access_denied";
    public const string InvalidScope = "doctor_profile.invalid_scope";
    public const string ClinicScopeRequired = "doctor_profile.clinic_scope_required";
    public const string DoctorScopeRequired = "doctor_profile.doctor_scope_required";
    public const string ClinicNotFound = "doctor_profile.clinic_not_found";
    public const string DoctorNotFound = "doctor_profile.doctor_not_found";
    public const string EmptyUpdate = "doctor_profile.empty_update";
    public const string InvalidField = "doctor_profile.invalid_field";
    public const string ConcurrencyConflict = "doctor_profile.concurrency_conflict";
}

/// <summary>
/// Query for <c>GET/PATCH /api/v1/doctor/profile</c>.
/// <see cref="ClinicId"/> and <see cref="DoctorStaffMemberId"/> are required for PLATFORM_ADMIN
/// with explicit bypass; ignored for DOCTOR (membership is authoritative).
/// </summary>
public sealed class DoctorProfileQuery
{
    public Guid? ClinicId { get; init; }

    public Guid? DoctorStaffMemberId { get; init; }
}

public sealed class DoctorProfileResponse
{
    public required Guid StaffMemberId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public required Guid ClinicId { get; init; }

    public required string ClinicName { get; init; }

    public required string Email { get; init; }

    public required string Role { get; init; }

    public string? DisplayName { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? JobTitle { get; init; }

    public string? ContactPhone { get; init; }

    /// <summary>Clinic specialty (authoritative); read-only for Doctor self-profile.</summary>
    public string? Specialty { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int Version { get; init; }
}

/// <summary>
/// Partial Doctor self-profile update. Omitted properties (Specified=false) are left unchanged.
/// Role, email, clinic, active status, and specialty are not editable here.
/// </summary>
public sealed class UpdateDoctorProfileRequest
{
    public int ExpectedVersion { get; init; }

    private string? _displayName;
    private bool _displayNameSpecified;
    private string? _firstName;
    private bool _firstNameSpecified;
    private string? _lastName;
    private bool _lastNameSpecified;
    private string? _jobTitle;
    private bool _jobTitleSpecified;
    private string? _contactPhone;
    private bool _contactPhoneSpecified;

    public string? DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            _displayNameSpecified = true;
        }
    }

    [JsonIgnore]
    public bool DisplayNameSpecified => _displayNameSpecified;

    public string? FirstName
    {
        get => _firstName;
        set
        {
            _firstName = value;
            _firstNameSpecified = true;
        }
    }

    [JsonIgnore]
    public bool FirstNameSpecified => _firstNameSpecified;

    public string? LastName
    {
        get => _lastName;
        set
        {
            _lastName = value;
            _lastNameSpecified = true;
        }
    }

    [JsonIgnore]
    public bool LastNameSpecified => _lastNameSpecified;

    public string? JobTitle
    {
        get => _jobTitle;
        set
        {
            _jobTitle = value;
            _jobTitleSpecified = true;
        }
    }

    [JsonIgnore]
    public bool JobTitleSpecified => _jobTitleSpecified;

    public string? ContactPhone
    {
        get => _contactPhone;
        set
        {
            _contactPhone = value;
            _contactPhoneSpecified = true;
        }
    }

    [JsonIgnore]
    public bool ContactPhoneSpecified => _contactPhoneSpecified;

    [JsonIgnore]
    public bool HasAnyEditableField =>
        DisplayNameSpecified
        || FirstNameSpecified
        || LastNameSpecified
        || JobTitleSpecified
        || ContactPhoneSpecified;
}
