using System.Net.Http.Json;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;

namespace HealthCare.Web.Services;

public interface IStaffOperationsApiClient
{
    Task<PagedResponse<AppointmentReminderResponse>> SearchRemindersAsync(
        StaffReminderSearchQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppointmentReminderResponse>> ListAppointmentRemindersAsync(
        Guid appointmentId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentReminderResponse> RetryReminderAsync(
        Guid appointmentId,
        Guid reminderId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<ClinicAppointmentSummaryRunResponse>> ListSummaryRunsAsync(
        ClinicAppointmentSummaryRunQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicAppointmentSummaryRunResponse> RetrySummaryAsync(
        Guid clinicId,
        DateOnly summaryDate,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicAppointmentSummaryResponse> GetClinicSummaryAsync(
        ClinicAppointmentSummaryQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<StaffOperationsHealthResponse> GetOperationsHealthAsync(
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class StaffOperationsApiClient : IStaffOperationsApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public StaffOperationsApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PagedResponse<AppointmentReminderResponse>> SearchRemindersAsync(
        StaffReminderSearchQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildReminderSearchQuery("api/v1/staff/reminders", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<AppointmentReminderResponse>>(cancellationToken))
               ?? PagedResponse<AppointmentReminderResponse>.Create([], query.Page, query.PageSize, 0);
    }

    public async Task<IReadOnlyList<AppointmentReminderResponse>> ListAppointmentRemindersAsync(
        Guid appointmentId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff/appointments/{appointmentId:D}/reminders", platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<AppointmentReminderResponse>>(cancellationToken)) ?? [];
    }

    public async Task<AppointmentReminderResponse> RetryReminderAsync(
        Guid appointmentId,
        Guid reminderId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff/appointments/{appointmentId:D}/reminders/retry", platformAdminBypass);
        using var response = await client.PostAsJsonAsync(
            url,
            new RetryAppointmentReminderRequest { ReminderId = reminderId },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AppointmentReminderResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid reminder retry response", null, null);
    }

    public async Task<PagedResponse<ClinicAppointmentSummaryRunResponse>> ListSummaryRunsAsync(
        ClinicAppointmentSummaryRunQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildSummaryRunQuery("api/v1/staff/appointment-summary-runs", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<ClinicAppointmentSummaryRunResponse>>(cancellationToken))
               ?? PagedResponse<ClinicAppointmentSummaryRunResponse>.Create([], query.Page, query.PageSize, 0);
    }

    public async Task<ClinicAppointmentSummaryRunResponse> RetrySummaryAsync(
        Guid clinicId,
        DateOnly summaryDate,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var date = summaryDate.ToString("yyyy-MM-dd");
        var url = AppendBypass(
            $"api/v1/staff/clinics/{clinicId:D}/appointment-summary/{date}/retry",
            platformAdminBypass);
        using var response = await client.PostAsync(url, content: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ClinicAppointmentSummaryRunResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid summary retry response", null, null);
    }

    public async Task<ClinicAppointmentSummaryResponse> GetClinicSummaryAsync(
        ClinicAppointmentSummaryQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildClinicSummaryQuery("api/v1/staff/clinics/current/appointment-summary", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ClinicAppointmentSummaryResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic summary response", null, null);
    }

    public async Task<StaffOperationsHealthResponse> GetOperationsHealthAsync(
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass("api/v1/staff/operations/health", platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<StaffOperationsHealthResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid operations health response", null, null);
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

    private static string BuildReminderSearchQuery(string path, StaffReminderSearchQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"page={Math.Max(1, query.Page)}",
            $"pageSize={Math.Clamp(query.PageSize, 1, 200)}",
        };

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            parts.Add($"status={Uri.EscapeDataString(query.Status.Trim())}");
        }

        if (query.FromUtc is DateTimeOffset from)
        {
            parts.Add($"fromUtc={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}");
        }

        if (query.ToUtc is DateTimeOffset to)
        {
            parts.Add($"toUtc={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }

    private static string BuildSummaryRunQuery(string path, ClinicAppointmentSummaryRunQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"page={Math.Max(1, query.Page)}",
            $"pageSize={Math.Clamp(query.PageSize, 1, 200)}",
        };

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            parts.Add($"status={Uri.EscapeDataString(query.Status.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.FromDate))
        {
            parts.Add($"fromDate={Uri.EscapeDataString(query.FromDate.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(query.ToDate))
        {
            parts.Add($"toDate={Uri.EscapeDataString(query.ToDate.Trim())}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }

    private static string BuildClinicSummaryQuery(string path, ClinicAppointmentSummaryQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Date))
        {
            parts.Add($"date={Uri.EscapeDataString(query.Date.Trim())}");
        }

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }
}
