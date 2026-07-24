namespace HealthCare.Web.Auth;

/// <summary>
/// Circuit-scoped Organization Admin clinic working context ("All clinics" = null).
/// Not authorization — API remains authoritative. Cleared on logout.
/// </summary>
public interface IClinicWorkingContext
{
    Guid? SelectedClinicId { get; }

    string? SelectedClinicName { get; }

    bool? SelectedClinicIsActive { get; }

    bool HasClinic { get; }

    void SelectClinic(Guid clinicId, string? name = null, bool? isActive = null);

    void ClearClinic();

    void Clear();

    event Action? Changed;
}

public sealed class ClinicWorkingContext : IClinicWorkingContext
{
    private Guid? _clinicId;
    private string? _clinicName;
    private bool? _isActive;

    public Guid? SelectedClinicId => _clinicId;

    public string? SelectedClinicName => _clinicName;

    public bool? SelectedClinicIsActive => _isActive;

    public bool HasClinic => _clinicId is Guid id && id != Guid.Empty;

    public event Action? Changed;

    public void SelectClinic(Guid clinicId, string? name = null, bool? isActive = null)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("ClinicId must be a non-empty GUID.", nameof(clinicId));
        }

        if (_clinicId == clinicId
            && string.Equals(_clinicName, name, StringComparison.Ordinal)
            && _isActive == isActive)
        {
            return;
        }

        _clinicId = clinicId;
        _clinicName = name;
        _isActive = isActive;
        Changed?.Invoke();
    }

    public void ClearClinic()
    {
        if (_clinicId is null)
        {
            return;
        }

        _clinicId = null;
        _clinicName = null;
        _isActive = null;
        Changed?.Invoke();
    }

    public void Clear() => ClearClinic();
}
