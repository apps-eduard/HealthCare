using System.Net.Http.Json;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Staff;

namespace HealthCare.Web.Services;

public interface IStaffManagementApiClient
{
    Task<PagedResponse<StaffSummaryResponse>> SearchAsync(
        StaffSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> GetByIdAsync(
        Guid staffMemberId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<CreateStaffResponse> CreateAsync(
        CreateStaffRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> ActivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> DeactivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffRoleInfoResponse>> ListRolesAsync(CancellationToken cancellationToken = default);

    Task AssignRoleAsync(
        Guid staffMemberId,
        string roleName,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task RemoveRoleAsync(
        Guid staffMemberId,
        string roleName,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class StaffManagementApiClient : IStaffManagementApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public StaffManagementApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PagedResponse<StaffSummaryResponse>> SearchAsync(
        StaffSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery("api/v1/staff-management/staff", request, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<StaffSummaryResponse>>(cancellationToken))
               ?? PagedResponse<StaffSummaryResponse>.Create([], request.Page, request.PageSize, 0);
    }

    public async Task<StaffDetailResponse> GetByIdAsync(
        Guid staffMemberId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff-management/staff/{staffMemberId:D}", platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StaffDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid staff detail response", null, null);
    }

    public async Task<CreateStaffResponse> CreateAsync(
        CreateStaffRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass("api/v1/staff-management/staff", platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CreateStaffResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid create staff response", null, null);
    }

    public async Task<StaffDetailResponse> ActivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff-management/staff/{staffMemberId:D}/activate", platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StaffDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid activation response", null, null);
    }

    public async Task<StaffDetailResponse> DeactivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff-management/staff/{staffMemberId:D}/deactivate", platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StaffDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid deactivation response", null, null);
    }

    public async Task<IReadOnlyList<StaffRoleInfoResponse>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync("api/v1/staff-management/roles", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<StaffRoleInfoResponse>>(cancellationToken))
               ?? [];
    }

    public async Task AssignRoleAsync(
        Guid staffMemberId,
        string roleName,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var encoded = Uri.EscapeDataString(roleName);
        var url = AppendBypass($"api/v1/staff-management/staff/{staffMemberId:D}/roles/{encoded}", platformAdminBypass);
        using var response = await client.PostAsync(url, content: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RemoveRoleAsync(
        Guid staffMemberId,
        string roleName,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var encoded = Uri.EscapeDataString(roleName);
        var url = AppendBypass($"api/v1/staff-management/staff/{staffMemberId:D}/roles/{encoded}", platformAdminBypass);
        using var response = await client.DeleteAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }
    }

    private static string AppendBypass(string path, bool platformAdminBypass) =>
        platformAdminBypass ? $"{path}?platformAdminBypass=true" : path;

    private static string BuildQuery(string path, StaffSearchRequest request, bool platformAdminBypass)
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

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            query.Add($"role={Uri.EscapeDataString(request.Role)}");
        }

        if (request.IsActive is bool active)
        {
            query.Add($"isActive={active.ToString().ToLowerInvariant()}");
        }

        if (request.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            query.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            query.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', query)}";
    }
}
