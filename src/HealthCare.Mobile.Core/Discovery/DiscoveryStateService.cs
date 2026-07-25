using HealthCare.Contracts.Appointments;

namespace HealthCare.Mobile.Core.Discovery;

/// <summary>
/// In-memory discovery selection for PM-4. Cleared on logout.
/// A selected slot is not a reservation.
/// </summary>
public sealed record DiscoverySelection
{
    public string? ClinicCode { get; init; }

    public string? ClinicName { get; init; }

    public Guid? DoctorStaffMemberId { get; init; }

    public string? DoctorDisplayName { get; init; }

    public DateOnly? SlotDate { get; init; }

    public AvailableSlotResponse? SelectedSlot { get; init; }

    public bool HasSlot => SelectedSlot is not null;
}

public interface IDiscoveryStateService
{
    DiscoverySelection Current { get; }

    event Action? Changed;

    void SelectClinic(string clinicCode, string? clinicName);

    void SelectDoctor(Guid staffMemberId, string? displayName);

    void SelectSlot(DateOnly date, AvailableSlotResponse slot);

    void ClearDoctorAndSlot();

    void ClearSlot();

    void Clear();
}

public sealed class DiscoveryStateService : IDiscoveryStateService
{
    private readonly object _gate = new();
    private DiscoverySelection _current = new();

    public event Action? Changed;

    public DiscoverySelection Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void SelectClinic(string clinicCode, string? clinicName)
    {
        lock (_gate)
        {
            var code = clinicCode.Trim();
            if (!string.Equals(_current.ClinicCode, code, StringComparison.OrdinalIgnoreCase))
            {
                _current = new DiscoverySelection
                {
                    ClinicCode = code,
                    ClinicName = clinicName,
                };
            }
            else
            {
                _current = _current with { ClinicName = clinicName ?? _current.ClinicName };
            }
        }

        Changed?.Invoke();
    }

    public void SelectDoctor(Guid staffMemberId, string? displayName)
    {
        lock (_gate)
        {
            if (_current.DoctorStaffMemberId != staffMemberId)
            {
                _current = new DiscoverySelection
                {
                    ClinicCode = _current.ClinicCode,
                    ClinicName = _current.ClinicName,
                    DoctorStaffMemberId = staffMemberId,
                    DoctorDisplayName = displayName,
                };
            }
            else
            {
                _current = _current with { DoctorDisplayName = displayName ?? _current.DoctorDisplayName };
            }
        }

        Changed?.Invoke();
    }

    public void SelectSlot(DateOnly date, AvailableSlotResponse slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        lock (_gate)
        {
            _current = _current with
            {
                SlotDate = date,
                SelectedSlot = slot,
            };
        }

        Changed?.Invoke();
    }

    public void ClearDoctorAndSlot()
    {
        lock (_gate)
        {
            _current = new DiscoverySelection
            {
                ClinicCode = _current.ClinicCode,
                ClinicName = _current.ClinicName,
            };
        }

        Changed?.Invoke();
    }

    public void ClearSlot()
    {
        lock (_gate)
        {
            _current = _current with { SlotDate = null, SelectedSlot = null };
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = new();
        }

        Changed?.Invoke();
    }
}
