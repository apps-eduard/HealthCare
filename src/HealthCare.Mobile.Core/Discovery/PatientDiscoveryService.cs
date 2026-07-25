using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;

namespace HealthCare.Mobile.Core.Discovery;

public interface IPatientDiscoveryService
{
    Task<ApiResult<PagedResponse<PatientClinicListItemResponse>>> SearchClinicsAsync(
        PatientClinicSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<PatientClinicDetailResponse>> GetClinicAsync(
        string clinicCode,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ClinicPatientEnrollmentResponse>> EnrollAsync(
        string clinicCode,
        CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<ClinicDoctorResponse>>> ListDoctorsAsync(
        string clinicCode,
        CancellationToken cancellationToken = default);

    Task<ApiResult<IReadOnlyList<AvailableSlotResponse>>> GetSlotsAsync(
        string clinicCode,
        Guid staffMemberId,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

public sealed class PatientDiscoveryService : IPatientDiscoveryService
{
    private readonly IHealthCareApiClient _api;

    public PatientDiscoveryService(IHealthCareApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<PagedResponse<PatientClinicListItemResponse>>> SearchClinicsAsync(
        PatientClinicSearchRequest request,
        CancellationToken cancellationToken = default) =>
        _api.SearchClinicsAsync(request, cancellationToken);

    public Task<ApiResult<PatientClinicDetailResponse>> GetClinicAsync(
        string clinicCode,
        CancellationToken cancellationToken = default) =>
        _api.GetClinicAsync(clinicCode, cancellationToken);

    public Task<ApiResult<ClinicPatientEnrollmentResponse>> EnrollAsync(
        string clinicCode,
        CancellationToken cancellationToken = default) =>
        _api.RegisterWithClinicAsync(
            new RegisterPatientWithClinicRequest { ClinicCode = clinicCode.Trim().ToLowerInvariant() },
            cancellationToken);

    public Task<ApiResult<IReadOnlyList<ClinicDoctorResponse>>> ListDoctorsAsync(
        string clinicCode,
        CancellationToken cancellationToken = default) =>
        _api.ListDoctorsAsync(clinicCode, cancellationToken);

    public Task<ApiResult<IReadOnlyList<AvailableSlotResponse>>> GetSlotsAsync(
        string clinicCode,
        Guid staffMemberId,
        DateOnly date,
        CancellationToken cancellationToken = default) =>
        _api.GetAvailableSlotsAsync(clinicCode, staffMemberId, date, cancellationToken: cancellationToken);
}

/// <summary>
/// Displays API UTC/local slot times. Clinic-local strings from the API are preferred when present;
/// otherwise device-local conversion of StartUtc is used as a fallback.
/// </summary>
public static class SlotDisplay
{
    public static string FormatRange(AvailableSlotResponse slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.StartLocal) && !string.IsNullOrWhiteSpace(slot.EndLocal))
        {
            var tz = string.IsNullOrWhiteSpace(slot.TimeZoneId) ? string.Empty : $" ({slot.TimeZoneId})";
            return $"{slot.StartLocal} – {slot.EndLocal}{tz}";
        }

        var start = slot.StartUtc.ToLocalTime();
        var end = slot.EndUtc.ToLocalTime();
        return $"{start:t} – {end:t} (device local)";
    }
}
