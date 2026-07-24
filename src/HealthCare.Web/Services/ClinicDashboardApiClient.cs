using System.Net.Http.Json;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Web.Services;

public interface IClinicDashboardApiClient
{
    Task<ClinicDashboardResponse> GetAsync(
        ClinicDashboardQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicDashboardApiClient : IClinicDashboardApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClinicDashboardApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ClinicDashboardResponse> GetAsync(
        ClinicDashboardQuery query,
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

        return (await response.Content.ReadFromJsonAsync<ClinicDashboardResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic dashboard response", null, null);
    }

    private static string BuildQuery(ClinicDashboardQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (!string.IsNullOrWhiteSpace(query.Date))
        {
            parts.Add($"date={Uri.EscapeDataString(query.Date)}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return parts.Count == 0
            ? "api/v1/clinic/dashboard"
            : "api/v1/clinic/dashboard?" + string.Join('&', parts);
    }
}
