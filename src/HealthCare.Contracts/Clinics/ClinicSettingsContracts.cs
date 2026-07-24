using System.Text.Json.Serialization;

namespace HealthCare.Contracts.Clinics;

public static class ClinicSettingsErrorCodes
{
    public const string AccessDenied = "clinic_settings.access_denied";
    public const string InvalidScope = "clinic_settings.invalid_scope";
    public const string ClinicScopeRequired = "clinic_settings.clinic_scope_required";
    public const string ClinicNotFound = "clinic_settings.clinic_not_found";
    public const string EmptyUpdate = "clinic_settings.empty_update";
    public const string InvalidTimezone = "clinic_settings.invalid_timezone";
    public const string ConcurrencyConflict = "clinic_settings.concurrency_conflict";
}

/// <summary>
/// Query for <c>GET/PATCH /api/v1/clinic/settings</c>.
/// <see cref="ClinicId"/> is required for PLATFORM_ADMIN with explicit bypass; ignored for CLINIC_ADMIN
/// (membership clinic is authoritative).
/// </summary>
public sealed class ClinicSettingsQuery
{
    public Guid? ClinicId { get; init; }
}

public sealed class ClinicSettingsResponse
{
    public Guid ClinicId { get; init; }

    public Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? Specialty { get; init; }

    public string? ContactEmail { get; init; }

    public string? ContactPhone { get; init; }

    public string? Address { get; init; }

    public string? City { get; init; }

    public string? Country { get; init; }

    public required string DefaultTimeZoneId { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public int Version { get; init; }
}

/// <summary>
/// Partial clinic profile update. Omitted properties (Specified=false) are left unchanged.
/// Slug / IsActive / OrganizationId are not editable here.
/// </summary>
public sealed class UpdateClinicSettingsRequest
{
    public int ExpectedVersion { get; init; }

    private string? _name;
    private bool _nameSpecified;
    private string? _specialty;
    private bool _specialtySpecified;
    private string? _contactEmail;
    private bool _contactEmailSpecified;
    private string? _contactPhone;
    private bool _contactPhoneSpecified;
    private string? _address;
    private bool _addressSpecified;
    private string? _city;
    private bool _citySpecified;
    private string? _country;
    private bool _countrySpecified;
    private string? _defaultTimeZoneId;
    private bool _defaultTimeZoneIdSpecified;

    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _nameSpecified = true;
        }
    }

    [JsonIgnore]
    public bool NameSpecified => _nameSpecified;

    public string? Specialty
    {
        get => _specialty;
        set
        {
            _specialty = value;
            _specialtySpecified = true;
        }
    }

    [JsonIgnore]
    public bool SpecialtySpecified => _specialtySpecified;

    public string? ContactEmail
    {
        get => _contactEmail;
        set
        {
            _contactEmail = value;
            _contactEmailSpecified = true;
        }
    }

    [JsonIgnore]
    public bool ContactEmailSpecified => _contactEmailSpecified;

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

    public string? Address
    {
        get => _address;
        set
        {
            _address = value;
            _addressSpecified = true;
        }
    }

    [JsonIgnore]
    public bool AddressSpecified => _addressSpecified;

    public string? City
    {
        get => _city;
        set
        {
            _city = value;
            _citySpecified = true;
        }
    }

    [JsonIgnore]
    public bool CitySpecified => _citySpecified;

    public string? Country
    {
        get => _country;
        set
        {
            _country = value;
            _countrySpecified = true;
        }
    }

    [JsonIgnore]
    public bool CountrySpecified => _countrySpecified;

    public string? DefaultTimeZoneId
    {
        get => _defaultTimeZoneId;
        set
        {
            _defaultTimeZoneId = value;
            _defaultTimeZoneIdSpecified = true;
        }
    }

    [JsonIgnore]
    public bool DefaultTimeZoneIdSpecified => _defaultTimeZoneIdSpecified;

    [JsonIgnore]
    public bool HasAnyEditableField =>
        NameSpecified
        || SpecialtySpecified
        || ContactEmailSpecified
        || ContactPhoneSpecified
        || AddressSpecified
        || CitySpecified
        || CountrySpecified
        || DefaultTimeZoneIdSpecified;
}
