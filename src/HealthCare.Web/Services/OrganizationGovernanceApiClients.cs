using System.Net.Http.Json;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Web.Services;

public interface IOrganizationAuditLogApiClient
{
    Task<OrganizationAuditLogListResponse> SearchAsync(
        OrganizationAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        OrganizationAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationAuditLogListResponse> GetByCorrelationIdAsync(
        string correlationId,
        OrganizationAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public interface IOrganizationUsageApiClient
{
    Task<OrganizationUsageResponse> GetUsageAsync(
        OrganizationUsageQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class OrganizationAuditLogApiClient : IOrganizationAuditLogApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrganizationAuditLogApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OrganizationAuditLogListResponse> SearchAsync(
        OrganizationAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery("api/v1/organization/audit-logs", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<OrganizationAuditLogListResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid audit log list response", null, null);
    }

    public async Task<OrganizationAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        OrganizationAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery($"api/v1/organization/audit-logs/{eventId:D}", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<OrganizationAuditLogDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid audit log detail response", null, null);
    }

    public async Task<OrganizationAuditLogListResponse> GetByCorrelationIdAsync(
        string correlationId,
        OrganizationAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var encoded = Uri.EscapeDataString(correlationId.Trim());
        var url = BuildQuery($"api/v1/organization/audit-logs/by-correlation/{encoded}", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<OrganizationAuditLogListResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid audit correlation response", null, null);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }
    }

    private static string BuildQuery(string path, OrganizationAuditLogQuery query, bool platformAdminBypass)
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

        if (query.ActorUserId is Guid actorId && actorId != Guid.Empty)
        {
            parts.Add($"actorUserId={actorId:D}");
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            parts.Add($"category={Uri.EscapeDataString(query.Category.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            parts.Add($"action={Uri.EscapeDataString(query.Action.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.ResultCode))
        {
            parts.Add($"resultCode={Uri.EscapeDataString(query.ResultCode.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            parts.Add($"correlationId={Uri.EscapeDataString(query.CorrelationId.Trim())}");
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

public sealed class OrganizationUsageApiClient : IOrganizationUsageApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrganizationUsageApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OrganizationUsageResponse> GetUsageAsync(
        OrganizationUsageQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var parts = new List<string>();
        if (query.OrganizationId is Guid orgId && orgId != Guid.Empty)
        {
            parts.Add($"organizationId={orgId:D}");
        }

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        var url = parts.Count == 0
            ? "api/v1/organization/usage"
            : "api/v1/organization/usage?" + string.Join('&', parts);

        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<OrganizationUsageResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid organization usage response", null, null);
    }
}
