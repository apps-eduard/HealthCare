using System.Net.Http.Json;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;

namespace HealthCare.Web.Services;

public interface IClinicDirectoryApiClient
{
    Task<PagedResponse<ClinicDirectoryItemResponse>> SearchAsync(
        ClinicSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicDirectoryApiClient : IClinicDirectoryApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClinicDirectoryApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PagedResponse<ClinicDirectoryItemResponse>> SearchAsync(
        ClinicSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery(request, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<PagedResponse<ClinicDirectoryItemResponse>>(cancellationToken))
               ?? PagedResponse<ClinicDirectoryItemResponse>.Create([], request.Page, request.PageSize, 0);
    }

    public async Task<ClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = platformAdminBypass
            ? $"api/v1/staff-management/clinics/{clinicId:D}?platformAdminBypass=true"
            : $"api/v1/staff-management/clinics/{clinicId:D}";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<ClinicDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic detail response", null, null);
    }

    private static string BuildQuery(ClinicSearchRequest request, bool platformAdminBypass)
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

        if (request.OrganizationId is Guid orgId && orgId != Guid.Empty)
        {
            query.Add($"organizationId={orgId:D}");
        }

        if (platformAdminBypass)
        {
            query.Add("platformAdminBypass=true");
        }

        return $"api/v1/staff-management/clinics?{string.Join('&', query)}";
    }
}
