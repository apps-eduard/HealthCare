using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HealthCare.Contracts.Doctors;

namespace HealthCare.Web.Services;

public interface IDoctorProfileApiClient
{
    Task<DoctorProfileResponse> GetAsync(
        DoctorProfileQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<DoctorProfileResponse> UpdateAsync(
        UpdateDoctorProfileRequest request,
        DoctorProfileQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorProfileApiClient : IDoctorProfileApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public DoctorProfileApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DoctorProfileResponse> GetAsync(
        DoctorProfileQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery(query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<DoctorProfileResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid doctor profile response", null, null);
    }

    public async Task<DoctorProfileResponse> UpdateAsync(
        UpdateDoctorProfileRequest request,
        DoctorProfileQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery(query, platformAdminBypass);
        using var response = await client.PatchAsJsonAsync(url, request, SerializerOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<DoctorProfileResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid doctor profile update response", null, null);
    }

    private static string BuildQuery(DoctorProfileQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (query.DoctorStaffMemberId is Guid doctorId && doctorId != Guid.Empty)
        {
            parts.Add($"doctorStaffMemberId={doctorId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return parts.Count == 0
            ? "api/v1/doctor/profile"
            : "api/v1/doctor/profile?" + string.Join('&', parts);
    }
}
