using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Web.Services;

public interface IClinicSettingsApiClient
{
    Task<ClinicSettingsResponse> GetAsync(
        ClinicSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicSettingsResponse> UpdateAsync(
        UpdateClinicSettingsRequest request,
        ClinicSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicSettingsApiClient : IClinicSettingsApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public ClinicSettingsApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ClinicSettingsResponse> GetAsync(
        ClinicSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery("api/v1/clinic/settings", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<ClinicSettingsResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic settings response", null, null);
    }

    public async Task<ClinicSettingsResponse> UpdateAsync(
        UpdateClinicSettingsRequest request,
        ClinicSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery("api/v1/clinic/settings", query, platformAdminBypass);
        using var response = await client.PatchAsJsonAsync(url, request, SerializerOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<ClinicSettingsResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic settings update response", null, null);
    }

    private static string BuildQuery(string path, ClinicSettingsQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }
}
