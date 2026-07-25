using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;

namespace HealthCare.Mobile.Core.Patients;

public interface IPatientProfileService
{
    Task<ApiResult<PatientProfileResponse>> GetAsync(CancellationToken cancellationToken = default);

    Task<ApiResult<PatientProfileResponse>> UpdateAsync(
        UpdatePatientProfileRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PatientProfileService : IPatientProfileService
{
    private readonly IHealthCareApiClient _api;

    public PatientProfileService(IHealthCareApiClient api)
    {
        _api = api;
    }

    public Task<ApiResult<PatientProfileResponse>> GetAsync(CancellationToken cancellationToken = default) =>
        _api.GetPatientProfileAsync(cancellationToken);

    public Task<ApiResult<PatientProfileResponse>> UpdateAsync(
        UpdatePatientProfileRequest request,
        CancellationToken cancellationToken = default) =>
        _api.UpdatePatientProfileAsync(request, cancellationToken);
}
