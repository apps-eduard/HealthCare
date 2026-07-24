namespace HealthCare.Contracts.Organizations;

public static class OrganizationSettingsErrorCodes
{
    public const string AccessDenied = "organization_settings.access_denied";
    public const string InvalidScope = "organization_settings.invalid_scope";
    public const string OrganizationScopeRequired = "organization_settings.organization_scope_required";
    public const string OrganizationNotFound = "organization_settings.organization_not_found";
    public const string EmptyUpdate = "organization_settings.empty_update";
    public const string InvalidTimezone = "organization_settings.invalid_timezone";
    public const string ConcurrencyConflict = "organization_settings.concurrency_conflict";
}

public sealed class OrganizationSettingsQuery
{
    public Guid? OrganizationId { get; init; }
}

public sealed class OrganizationSettingsResponse
{
    public Guid OrganizationId { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public required string Status { get; init; }

    public string? ContactEmail { get; init; }

    public string? ContactPhone { get; init; }

    public string? Country { get; init; }

    public string? DefaultTimeZoneId { get; init; }

    public string? BrandingPlaceholder { get; init; }

    public int Version { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>Platform-enforced max clinics (resolved effective value).</summary>
    public int MaxClinics { get; init; }

    /// <summary>Platform-enforced max staff (resolved effective value).</summary>
    public int MaxStaff { get; init; }

    public int ClinicCount { get; init; }

    public int StaffCount { get; init; }

    public int RemainingClinicCapacity { get; init; }

    public int RemainingStaffCapacity { get; init; }
}

/// <summary>
/// Partial organization profile update. Omitted properties (Specified=false) are left unchanged.
/// MaxClinics / MaxStaff / Status / Slug are not editable here.
/// </summary>
public sealed class UpdateOrganizationSettingsRequest
{
    public int ExpectedVersion { get; init; }

    private string? _name;
    private bool _nameSpecified;
    private string? _contactEmail;
    private bool _contactEmailSpecified;
    private string? _contactPhone;
    private bool _contactPhoneSpecified;
    private string? _country;
    private bool _countrySpecified;
    private string? _defaultTimeZoneId;
    private bool _defaultTimeZoneIdSpecified;
    private string? _brandingPlaceholder;
    private bool _brandingPlaceholderSpecified;

    public string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _nameSpecified = true;
        }
    }

    public bool NameSpecified => _nameSpecified;

    public string? ContactEmail
    {
        get => _contactEmail;
        set
        {
            _contactEmail = value;
            _contactEmailSpecified = true;
        }
    }

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

    public bool ContactPhoneSpecified => _contactPhoneSpecified;

    public string? Country
    {
        get => _country;
        set
        {
            _country = value;
            _countrySpecified = true;
        }
    }

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

    public bool DefaultTimeZoneIdSpecified => _defaultTimeZoneIdSpecified;

    public string? BrandingPlaceholder
    {
        get => _brandingPlaceholder;
        set
        {
            _brandingPlaceholder = value;
            _brandingPlaceholderSpecified = true;
        }
    }

    public bool BrandingPlaceholderSpecified => _brandingPlaceholderSpecified;

    public bool HasAnyEditableField =>
        NameSpecified
        || ContactEmailSpecified
        || ContactPhoneSpecified
        || CountrySpecified
        || DefaultTimeZoneIdSpecified
        || BrandingPlaceholderSpecified;
}
