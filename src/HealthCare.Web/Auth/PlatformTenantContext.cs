namespace HealthCare.Web.Auth;

/// <summary>
/// Circuit-scoped PLATFORM_ADMIN tenant selection. Not authorization — API remains authoritative.
/// </summary>
public interface IPlatformTenantContext
{
    Guid? SelectedOrganizationId { get; }

    string? SelectedOrganizationName { get; }

    string? SelectedOrganizationSlug { get; }

    Guid? SelectedClinicId { get; }

    string? SelectedClinicName { get; }

    /// <summary>
    /// True when PLATFORM_ADMIN has explicitly selected an organization for scoped API calls.
    /// Does not grant access by itself; callers must still send platformAdminBypass where required.
    /// </summary>
    bool ExplicitBypassEnabled { get; }

    bool HasOrganization { get; }

    bool HasClinic { get; }

    void SelectOrganization(Guid organizationId, string name, string? slug = null);

    void SelectClinic(Guid clinicId, string? name = null);

    void ClearClinic();

    void Clear();

    event Action? Changed;
}

public sealed class PlatformTenantContext : IPlatformTenantContext
{
    private readonly ILogger<PlatformTenantContext> _logger;
    private Guid? _organizationId;
    private string? _organizationName;
    private string? _organizationSlug;
    private Guid? _clinicId;
    private string? _clinicName;

    public PlatformTenantContext(ILogger<PlatformTenantContext> logger)
    {
        _logger = logger;
    }

    public Guid? SelectedOrganizationId => _organizationId;

    public string? SelectedOrganizationName => _organizationName;

    public string? SelectedOrganizationSlug => _organizationSlug;

    public Guid? SelectedClinicId => _clinicId;

    public string? SelectedClinicName => _clinicName;

    public bool ExplicitBypassEnabled =>
        _organizationId is Guid id && id != Guid.Empty;

    public bool HasOrganization => ExplicitBypassEnabled;

    public bool HasClinic =>
        _clinicId is Guid clinicId && clinicId != Guid.Empty;

    public event Action? Changed;

    public void SelectOrganization(Guid organizationId, string name, string? slug = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("OrganizationId must be a non-empty GUID.", nameof(organizationId));
        }

        var previousOrg = _organizationId;
        var previousClinic = _clinicId;

        _organizationId = organizationId;
        _organizationName = name;
        _organizationSlug = slug;
        // Changing organization always clears clinic — never retain cross-org clinic.
        _clinicId = null;
        _clinicName = null;

        _logger.LogInformation(
            "Platform tenant selected. OrganizationId={OrganizationId} PreviousOrganizationId={PreviousOrganizationId} ClearedClinicId={ClearedClinicId}",
            organizationId,
            previousOrg,
            previousClinic);

        Changed?.Invoke();
    }

    public void SelectClinic(Guid clinicId, string? name = null)
    {
        if (!HasOrganization)
        {
            throw new InvalidOperationException("Select an organization before selecting a clinic.");
        }

        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("ClinicId must be a non-empty GUID.", nameof(clinicId));
        }

        _clinicId = clinicId;
        _clinicName = name;

        _logger.LogInformation(
            "Platform clinic selected. OrganizationId={OrganizationId} ClinicId={ClinicId}",
            _organizationId,
            clinicId);

        Changed?.Invoke();
    }

    public void ClearClinic()
    {
        if (_clinicId is null)
        {
            return;
        }

        var cleared = _clinicId;
        _clinicId = null;
        _clinicName = null;

        _logger.LogInformation(
            "Platform clinic cleared. OrganizationId={OrganizationId} ClearedClinicId={ClearedClinicId}",
            _organizationId,
            cleared);

        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_organizationId is null && _clinicId is null)
        {
            return;
        }

        var clearedOrg = _organizationId;
        var clearedClinic = _clinicId;
        _organizationId = null;
        _organizationName = null;
        _organizationSlug = null;
        _clinicId = null;
        _clinicName = null;

        _logger.LogInformation(
            "Platform tenant cleared. ClearedOrganizationId={ClearedOrganizationId} ClearedClinicId={ClearedClinicId}",
            clearedOrg,
            clearedClinic);

        Changed?.Invoke();
    }
}
