using System.Net.Http.Json;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;

namespace HealthCare.Web.Services;

public interface IAppointmentApiClient
{
    Task<PagedResponse<AppointmentResponse>> ListStaffAsync(
        AppointmentListQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<AppointmentResponse>> ListQueueAsync(
        AppointmentQueueQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<AppointmentResponse>> ListCalendarAsync(
        AppointmentCalendarQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> GetByIdAsync(
        Guid appointmentId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CreateStaffAsync(
        CreateStaffAppointmentRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> ConfirmAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CheckInAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CompleteAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> MarkNoShowAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> CancelAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<AppointmentResponse> RescheduleAsync(
        Guid appointmentId,
        RescheduleAppointmentRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClinicDoctorResponse>> ListClinicDoctorsAsync(
        string clinicCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClinicDoctorResponse>> ListClinicDoctorsByIdAsync(
        Guid clinicId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(
        string clinicCode,
        Guid doctorStaffMemberId,
        DateOnly date,
        int? durationMinutes = null,
        CancellationToken cancellationToken = default);
}

public sealed class AppointmentApiClient : IAppointmentApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public AppointmentApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PagedResponse<AppointmentResponse>> ListStaffAsync(
        AppointmentListQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildListQuery("api/v1/staff/appointments", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<AppointmentResponse>>(cancellationToken))
               ?? PagedResponse<AppointmentResponse>.Create([], query.Page, query.PageSize, 0);
    }

    public async Task<PagedResponse<AppointmentResponse>> ListQueueAsync(
        AppointmentQueueQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQueueQuery("api/v1/staff/appointments/queue", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<AppointmentResponse>>(cancellationToken))
               ?? PagedResponse<AppointmentResponse>.Create([], query.Page, query.PageSize, 0);
    }

    public async Task<PagedResponse<AppointmentResponse>> ListCalendarAsync(
        AppointmentCalendarQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildCalendarQuery("api/v1/staff/appointments/calendar", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<AppointmentResponse>>(cancellationToken))
               ?? PagedResponse<AppointmentResponse>.Create([], query.Page, query.PageSize, 0);
    }

    public async Task<AppointmentResponse> GetByIdAsync(
        Guid appointmentId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/appointments/{appointmentId:D}", platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AppointmentResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid appointment response", null, null);
    }

    public async Task<AppointmentResponse> CreateStaffAsync(
        CreateStaffAppointmentRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass("api/v1/staff/appointments", platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AppointmentResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid create appointment response", null, null);
    }

    public Task<AppointmentResponse> ConfirmAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/v1/staff/appointments/{appointmentId:D}/confirm", request, platformAdminBypass, cancellationToken);

    public Task<AppointmentResponse> CheckInAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/v1/staff/appointments/{appointmentId:D}/check-in", request, platformAdminBypass, cancellationToken);

    public Task<AppointmentResponse> CompleteAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/v1/staff/appointments/{appointmentId:D}/complete", request, platformAdminBypass, cancellationToken);

    public Task<AppointmentResponse> MarkNoShowAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/v1/staff/appointments/{appointmentId:D}/no-show", request, platformAdminBypass, cancellationToken);

    public Task<AppointmentResponse> CancelAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        PostActionAsync($"api/v1/appointments/{appointmentId:D}/cancel", request, platformAdminBypass, cancellationToken);

    public async Task<AppointmentResponse> RescheduleAsync(
        Guid appointmentId,
        RescheduleAppointmentRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/appointments/{appointmentId:D}/reschedule", platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AppointmentResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid reschedule response", null, null);
    }

    public async Task<IReadOnlyList<ClinicDoctorResponse>> ListClinicDoctorsAsync(
        string clinicCode,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var encoded = Uri.EscapeDataString(clinicCode);
        using var response = await client.GetAsync($"api/v1/clinics/{encoded}/doctors", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<ClinicDoctorResponse>>(cancellationToken)) ?? [];
    }

    public async Task<IReadOnlyList<ClinicDoctorResponse>> ListClinicDoctorsByIdAsync(
        Guid clinicId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff/clinics/{clinicId:D}/doctors", platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<ClinicDoctorResponse>>(cancellationToken)) ?? [];
    }

    public async Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(
        string clinicCode,
        Guid doctorStaffMemberId,
        DateOnly date,
        int? durationMinutes = null,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var encoded = Uri.EscapeDataString(clinicCode);
        var url = $"api/v1/clinics/{encoded}/doctors/{doctorStaffMemberId:D}/available-slots?date={date:yyyy-MM-dd}";
        if (durationMinutes is int minutes)
        {
            url += $"&durationMinutes={minutes}";
        }

        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<AvailableSlotResponse>>(cancellationToken)) ?? [];
    }

    private async Task<AppointmentResponse> PostActionAsync(
        string path,
        AppointmentActionRequest request,
        bool platformAdminBypass,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass(path, platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AppointmentResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid appointment action response", null, null);
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

    private static string BuildListQuery(string path, AppointmentListQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"page={query.Page}",
            $"pageSize={query.PageSize}",
            $"sortBy={Uri.EscapeDataString(query.SortBy)}",
            $"sortDirection={Uri.EscapeDataString(query.SortDirection)}",
        };

        if (query.FromUtc is DateTimeOffset from)
        {
            parts.Add($"fromUtc={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}");
        }

        if (query.ToUtc is DateTimeOffset to)
        {
            parts.Add($"toUtc={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            parts.Add($"status={Uri.EscapeDataString(query.Status)}");
        }

        if (query.DoctorStaffMemberId is Guid doctorId && doctorId != Guid.Empty)
        {
            parts.Add($"doctorStaffMemberId={doctorId:D}");
        }

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }

    private static string BuildQueueQuery(string path, AppointmentQueueQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"page={query.Page}",
            $"pageSize={query.PageSize}",
        };

        if (query.FromUtc is DateTimeOffset from)
        {
            parts.Add($"fromUtc={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}");
        }

        if (query.ToUtc is DateTimeOffset to)
        {
            parts.Add($"toUtc={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}");
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            parts.Add($"status={Uri.EscapeDataString(query.Status)}");
        }

        if (query.DoctorStaffMemberId is Guid doctorId && doctorId != Guid.Empty)
        {
            parts.Add($"doctorStaffMemberId={doctorId:D}");
        }

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }

    private static string BuildCalendarQuery(string path, AppointmentCalendarQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"fromUtc={Uri.EscapeDataString(query.FromUtc.UtcDateTime.ToString("O"))}",
            $"toUtc={Uri.EscapeDataString(query.ToUtc.UtcDateTime.ToString("O"))}",
            $"view={Uri.EscapeDataString(query.View)}",
            $"page={query.Page}",
            $"pageSize={query.PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            parts.Add($"status={Uri.EscapeDataString(query.Status)}");
        }

        if (query.DoctorStaffMemberId is Guid doctorId && doctorId != Guid.Empty)
        {
            parts.Add($"doctorStaffMemberId={doctorId:D}");
        }

        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }
}
