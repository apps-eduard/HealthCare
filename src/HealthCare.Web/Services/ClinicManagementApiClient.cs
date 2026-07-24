using System.Net.Http.Json;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;

namespace HealthCare.Web.Services;

public interface IClinicManagementApiClient
{
    Task<PagedResponse<OrganizationClinicListItemResponse>> SearchAsync(
        OrganizationClinicSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> CreateAsync(
        CreateOrganizationClinicRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> UpdateAsync(
        Guid clinicId,
        UpdateOrganizationClinicRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> ActivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> DeactivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimeZoneInfoResponse>> ListTimeZonesAsync(CancellationToken cancellationToken = default);
}

public sealed class ClinicManagementApiClient : IClinicManagementApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClinicManagementApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PagedResponse<OrganizationClinicListItemResponse>> SearchAsync(
        OrganizationClinicSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildSearchQuery(request, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<PagedResponse<OrganizationClinicListItemResponse>>(cancellationToken))
               ?? PagedResponse<OrganizationClinicListItemResponse>.Create([], request.Page, request.PageSize, 0);
    }

    public async Task<OrganizationClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = platformAdminBypass
            ? $"api/v1/organization/clinics/{clinicId:D}?platformAdminBypass=true"
            : $"api/v1/organization/clinics/{clinicId:D}";
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationClinicDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic detail response", null, null);
    }

    public async Task<OrganizationClinicDetailResponse> CreateAsync(
        CreateOrganizationClinicRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = platformAdminBypass
            ? "api/v1/organization/clinics?platformAdminBypass=true"
            : "api/v1/organization/clinics";
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationClinicDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic create response", null, null);
    }

    public async Task<OrganizationClinicDetailResponse> UpdateAsync(
        Guid clinicId,
        UpdateOrganizationClinicRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = platformAdminBypass
            ? $"api/v1/organization/clinics/{clinicId:D}?platformAdminBypass=true"
            : $"api/v1/organization/clinics/{clinicId:D}";
        using var response = await client.PatchAsJsonAsync(url, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationClinicDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic update response", null, null);
    }

    public async Task<OrganizationClinicDetailResponse> ActivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = platformAdminBypass
            ? $"api/v1/organization/clinics/{clinicId:D}/activate?platformAdminBypass=true"
            : $"api/v1/organization/clinics/{clinicId:D}/activate";
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationClinicDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic activate response", null, null);
    }

    public async Task<OrganizationClinicDetailResponse> DeactivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = platformAdminBypass
            ? $"api/v1/organization/clinics/{clinicId:D}/deactivate?platformAdminBypass=true"
            : $"api/v1/organization/clinics/{clinicId:D}/deactivate";
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationClinicDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic deactivate response", null, null);
    }

    public async Task<IReadOnlyList<TimeZoneInfoResponse>> ListTimeZonesAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync("api/v1/reference/timezones", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<List<TimeZoneInfoResponse>>(cancellationToken))
               ?? [];
    }

    private static string BuildSearchQuery(OrganizationClinicSearchRequest request, bool platformAdminBypass)
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

        return "api/v1/organization/clinics?" + string.Join('&', query);
    }
}
