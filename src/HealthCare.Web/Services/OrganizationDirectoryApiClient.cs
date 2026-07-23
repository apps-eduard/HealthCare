using System.Net.Http.Json;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Web.Services;

public interface IOrganizationDirectoryApiClient
{
    Task<PagedResponse<OrganizationDirectoryItemResponse>> SearchOrganizationsAsync(
        OrganizationSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<OrganizationDetailResponse> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationDirectoryApiClient : IOrganizationDirectoryApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrganizationDirectoryApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PagedResponse<OrganizationDirectoryItemResponse>> SearchOrganizationsAsync(
        OrganizationSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery(request);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<PagedResponse<OrganizationDirectoryItemResponse>>(cancellationToken))
               ?? PagedResponse<OrganizationDirectoryItemResponse>.Create([], request.Page, request.PageSize, 0);
    }

    public async Task<OrganizationDetailResponse> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync(
            $"api/v1/platform/organizations/{organizationId:D}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid organization detail response", null, null);
    }

    private static string BuildQuery(OrganizationSearchRequest request)
    {
        var query = new List<string>
        {
            $"page={request.Page}",
            $"pageSize={request.PageSize}",
            $"sortBy={Uri.EscapeDataString(request.SortBy)}",
            $"sortDirection={Uri.EscapeDataString(request.SortDirection)}",
        };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            query.Add($"search={Uri.EscapeDataString(request.Search)}");
        }

        if (request.IsActive is bool active)
        {
            query.Add($"isActive={active.ToString().ToLowerInvariant()}");
        }

        return $"api/v1/platform/organizations?{string.Join('&', query)}";
    }
}
