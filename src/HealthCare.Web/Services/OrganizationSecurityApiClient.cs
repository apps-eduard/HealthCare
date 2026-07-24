using System.Net.Http.Json;
using HealthCare.Contracts.Security;

namespace HealthCare.Web.Services;

public interface IOrganizationSecurityApiClient
{
    Task<OrganizationSecuritySessionListResponse> ListSessionsAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<RevokeOrganizationSessionsResponse> RevokeStaffSessionsAsync(
        Guid staffMemberId,
        RevokeOrganizationSessionsRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<CompromisedAccountResponseResult> RespondToCompromisedAccountAsync(
        Guid staffMemberId,
        CompromisedAccountResponseRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationFailedLoginSummaryResponse> GetFailedLoginSummaryAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationSecurityEventSummaryResponse> GetAuthorizationDenialSummaryAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationSecurityEventSummaryResponse> GetCrossClinicAttemptSummaryAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationSecurityApiClient : IOrganizationSecurityApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrganizationSecurityApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<OrganizationSecuritySessionListResponse> ListSessionsAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationSecuritySessionListResponse>("sessions", query, platformAdminBypass, cancellationToken);

    public async Task<RevokeOrganizationSessionsResponse> RevokeStaffSessionsAsync(
        Guid staffMemberId,
        RevokeOrganizationSessionsRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass(
            $"api/v1/organization/security/staff/{staffMemberId:D}/sessions/revoke",
            platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<RevokeOrganizationSessionsResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid revoke sessions response", null, null);
    }

    public async Task<CompromisedAccountResponseResult> RespondToCompromisedAccountAsync(
        Guid staffMemberId,
        CompromisedAccountResponseRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass(
            $"api/v1/organization/security/staff/{staffMemberId:D}/compromise-response",
            platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CompromisedAccountResponseResult>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid compromise response", null, null);
    }

    public Task<OrganizationFailedLoginSummaryResponse> GetFailedLoginSummaryAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationFailedLoginSummaryResponse>("failed-logins", query, platformAdminBypass, cancellationToken);

    public Task<OrganizationSecurityEventSummaryResponse> GetAuthorizationDenialSummaryAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationSecurityEventSummaryResponse>(
            "authorization-denials",
            query,
            platformAdminBypass,
            cancellationToken);

    public Task<OrganizationSecurityEventSummaryResponse> GetCrossClinicAttemptSummaryAsync(
        OrganizationSecurityQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationSecurityEventSummaryResponse>(
            "cross-clinic-attempts",
            query,
            platformAdminBypass,
            cancellationToken);

    private async Task<T> GetAsync<T>(
        string path,
        OrganizationSecurityQuery query,
        bool platformAdminBypass,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery($"api/v1/organization/security/{path}", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid organization security response", null, null);
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

    private static string BuildQuery(string path, OrganizationSecurityQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"page={Math.Max(1, query.Page)}",
            $"pageSize={Math.Clamp(query.PageSize, 1, 100)}",
        };

        if (query.OrganizationId is Guid orgId && orgId != Guid.Empty)
        {
            parts.Add($"organizationId={orgId:D}");
        }

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (query.StaffMemberId is Guid staffId && staffId != Guid.Empty)
        {
            parts.Add($"staffMemberId={staffId:D}");
        }

        if (query.UserId is Guid userId && userId != Guid.Empty)
        {
            parts.Add($"userId={userId:D}");
        }

        if (query.IncludeRevoked)
        {
            parts.Add("includeRevoked=true");
        }

        if (!string.IsNullOrWhiteSpace(query.FromUtc))
        {
            parts.Add($"fromUtc={Uri.EscapeDataString(query.FromUtc.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.ToUtc))
        {
            parts.Add($"toUtc={Uri.EscapeDataString(query.ToUtc.Trim())}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }
}
