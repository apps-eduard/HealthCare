using System.Net.Http.Json;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Web.Services;

public interface IClinicAuditLogApiClient
{
    Task<ClinicAuditLogListResponse> SearchAsync(
        ClinicAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        ClinicAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicAuditLogApiClient : IClinicAuditLogApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClinicAuditLogApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ClinicAuditLogListResponse> SearchAsync(
        ClinicAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync(
            BuildQuery("api/v1/clinic/audit-logs", query, platformAdminBypass),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<ClinicAuditLogListResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic audit response", null, null);
    }

    public async Task<ClinicAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        ClinicAuditLogQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        using var response = await client.GetAsync(
            BuildQuery($"api/v1/clinic/audit-logs/{eventId:D}", query, platformAdminBypass),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<ClinicAuditLogDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic audit detail response", null, null);
    }

    private static string BuildQuery(string path, ClinicAuditLogQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (query.ActorUserId is Guid actor && actor != Guid.Empty)
        {
            parts.Add($"actorUserId={actor:D}");
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

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            parts.Add($"resourceType={Uri.EscapeDataString(query.ResourceType.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            parts.Add($"correlationId={Uri.EscapeDataString(query.CorrelationId.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.FromDate))
        {
            parts.Add($"fromDate={Uri.EscapeDataString(query.FromDate.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.ToDate))
        {
            parts.Add($"toDate={Uri.EscapeDataString(query.ToDate.Trim())}");
        }

        parts.Add($"page={Math.Max(1, query.Page)}");
        parts.Add($"pageSize={Math.Clamp(query.PageSize, 1, 100)}");

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }
}
