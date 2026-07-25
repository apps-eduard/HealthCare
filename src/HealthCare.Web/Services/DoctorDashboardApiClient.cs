using System.Net.Http.Json;
using HealthCare.Contracts.Doctors;

namespace HealthCare.Web.Services;

public interface IDoctorDashboardApiClient
{
    Task<DoctorDashboardResponse> GetAsync(
        DoctorDashboardQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorDashboardApiClient : IDoctorDashboardApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DoctorDashboardApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DoctorDashboardResponse> GetAsync(
        DoctorDashboardQuery query,
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

        return (await response.Content.ReadFromJsonAsync<DoctorDashboardResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid doctor dashboard response", null, null);
    }

    private static string BuildQuery(DoctorDashboardQuery query, bool platformAdminBypass)
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

        if (!string.IsNullOrWhiteSpace(query.Date))
        {
            parts.Add($"date={Uri.EscapeDataString(query.Date)}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return parts.Count == 0
            ? "api/v1/doctor/dashboard"
            : "api/v1/doctor/dashboard?" + string.Join('&', parts);
    }
}
