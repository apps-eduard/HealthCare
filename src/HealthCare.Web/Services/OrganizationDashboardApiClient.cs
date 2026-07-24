using System.Net.Http.Json;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Web.Services;

public interface IOrganizationDashboardApiClient
{
    Task<OrganizationDashboardResponse> GetAsync(
        OrganizationDashboardQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationDashboardApiClient : IOrganizationDashboardApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrganizationDashboardApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OrganizationDashboardResponse> GetAsync(
        OrganizationDashboardQuery query,
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

        return (await response.Content.ReadFromJsonAsync<OrganizationDashboardResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid organization dashboard response", null, null);
    }

    private static string BuildQuery(OrganizationDashboardQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.OrganizationId is Guid orgId && orgId != Guid.Empty)
        {
            parts.Add($"organizationId={orgId:D}");
        }

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
            ? "api/v1/organization/dashboard"
            : "api/v1/organization/dashboard?" + string.Join('&', parts);
    }
}
