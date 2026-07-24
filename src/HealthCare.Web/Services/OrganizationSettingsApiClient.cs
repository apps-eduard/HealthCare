using System.Net.Http.Json;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Web.Services;

public interface IOrganizationSettingsApiClient
{
    Task<OrganizationSettingsResponse> GetAsync(
        OrganizationSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationSettingsResponse> UpdateAsync(
        UpdateOrganizationSettingsRequest request,
        OrganizationSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationSettingsApiClient : IOrganizationSettingsApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrganizationSettingsApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OrganizationSettingsResponse> GetAsync(
        OrganizationSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery("api/v1/organization/settings", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationSettingsResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid organization settings response", null, null);
    }

    public async Task<OrganizationSettingsResponse> UpdateAsync(
        UpdateOrganizationSettingsRequest request,
        OrganizationSettingsQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery("api/v1/organization/settings", query, platformAdminBypass);
        using var response = await client.PatchAsJsonAsync(url, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationSettingsResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid organization settings update response", null, null);
    }

    private static string BuildQuery(string path, OrganizationSettingsQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.OrganizationId is Guid orgId && orgId != Guid.Empty)
        {
            parts.Add($"organizationId={orgId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }
}
