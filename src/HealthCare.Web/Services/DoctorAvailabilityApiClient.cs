using System.Net.Http.Json;
using HealthCare.Contracts.Appointments;

namespace HealthCare.Web.Services;

public interface IDoctorAvailabilityApiClient
{
    Task<IReadOnlyList<ClinicDoctorResponse>> ListClinicDoctorsAsync(
        string clinicCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorAvailabilityResponse>> ListAvailabilityAsync(
        Guid doctorStaffMemberId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<DoctorAvailabilityResponse> CreateAvailabilityAsync(
        Guid doctorStaffMemberId,
        CreateDoctorAvailabilityRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<DoctorAvailabilityResponse> UpdateAvailabilityAsync(
        Guid doctorStaffMemberId,
        Guid availabilityId,
        UpdateDoctorAvailabilityRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task DeleteAvailabilityAsync(
        Guid doctorStaffMemberId,
        Guid availabilityId,
        int expectedVersion,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorAvailabilityExceptionResponse>> ListExceptionsAsync(
        Guid doctorStaffMemberId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<DoctorAvailabilityExceptionResponse> CreateExceptionAsync(
        Guid doctorStaffMemberId,
        CreateDoctorAvailabilityExceptionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task DeleteExceptionAsync(
        Guid doctorStaffMemberId,
        Guid exceptionId,
        int expectedVersion,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AvailableSlotResponse>> GetAvailableSlotsAsync(
        string clinicCode,
        Guid doctorStaffMemberId,
        DateOnly date,
        int? durationMinutes = null,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorAvailabilityApiClient : IDoctorAvailabilityApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public DoctorAvailabilityApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
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

    public async Task<IReadOnlyList<DoctorAvailabilityResponse>> ListAvailabilityAsync(
        Guid doctorStaffMemberId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff/doctors/{doctorStaffMemberId:D}/availability", platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponse>>(cancellationToken)) ?? [];
    }

    public async Task<DoctorAvailabilityResponse> CreateAvailabilityAsync(
        Guid doctorStaffMemberId,
        CreateDoctorAvailabilityRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff/doctors/{doctorStaffMemberId:D}/availability", platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<DoctorAvailabilityResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid create availability response", null, null);
    }

    public async Task<DoctorAvailabilityResponse> UpdateAvailabilityAsync(
        Guid doctorStaffMemberId,
        Guid availabilityId,
        UpdateDoctorAvailabilityRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass(
            $"api/v1/staff/doctors/{doctorStaffMemberId:D}/availability/{availabilityId:D}",
            platformAdminBypass);
        using var response = await client.PatchAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<DoctorAvailabilityResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid update availability response", null, null);
    }

    public async Task DeleteAvailabilityAsync(
        Guid doctorStaffMemberId,
        Guid availabilityId,
        int expectedVersion,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url =
            $"api/v1/staff/doctors/{doctorStaffMemberId:D}/availability/{availabilityId:D}?expectedVersion={expectedVersion}";
        if (platformAdminBypass)
        {
            url += "&platformAdminBypass=true";
        }

        using var response = await client.DeleteAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorAvailabilityExceptionResponse>> ListExceptionsAsync(
        Guid doctorStaffMemberId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass(
            $"api/v1/staff/doctors/{doctorStaffMemberId:D}/availability-exceptions",
            platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<DoctorAvailabilityExceptionResponse>>(cancellationToken))
               ?? [];
    }

    public async Task<DoctorAvailabilityExceptionResponse> CreateExceptionAsync(
        Guid doctorStaffMemberId,
        CreateDoctorAvailabilityExceptionRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass(
            $"api/v1/staff/doctors/{doctorStaffMemberId:D}/availability-exceptions",
            platformAdminBypass);
        using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<DoctorAvailabilityExceptionResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid create exception response", null, null);
    }

    public async Task DeleteExceptionAsync(
        Guid doctorStaffMemberId,
        Guid exceptionId,
        int expectedVersion,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url =
            $"api/v1/staff/doctors/{doctorStaffMemberId:D}/availability-exceptions/{exceptionId:D}?expectedVersion={expectedVersion}";
        if (platformAdminBypass)
        {
            url += "&platformAdminBypass=true";
        }

        using var response = await client.DeleteAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
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
        var url =
            $"api/v1/clinics/{encoded}/doctors/{doctorStaffMemberId:D}/available-slots?date={date:yyyy-MM-dd}";
        if (durationMinutes is int minutes)
        {
            url += $"&durationMinutes={minutes}";
        }

        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<List<AvailableSlotResponse>>(cancellationToken)) ?? [];
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
}
