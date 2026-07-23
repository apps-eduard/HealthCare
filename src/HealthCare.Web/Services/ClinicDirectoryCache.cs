using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;

namespace HealthCare.Web.Services;

/// <summary>
/// Short-lived, circuit-scoped clinic detail cache. Cleared on logout via PermissionState clear path.
/// </summary>
public interface IClinicDirectoryCache
{
    bool TryGet(Guid clinicId, out ClinicDetailResponse? clinic);

    void Set(ClinicDetailResponse clinic);

    void Clear();
}

public sealed class ClinicDirectoryCache : IClinicDirectoryCache
{
    private readonly Dictionary<Guid, ClinicDetailResponse> _items = new();

    public bool TryGet(Guid clinicId, out ClinicDetailResponse? clinic) =>
        _items.TryGetValue(clinicId, out clinic);

    public void Set(ClinicDetailResponse clinic) =>
        _items[clinic.ClinicId] = clinic;

    public void Clear() => _items.Clear();
}
