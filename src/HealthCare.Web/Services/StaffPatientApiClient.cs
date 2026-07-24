using System.Net.Http.Json;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Patients;

namespace HealthCare.Web.Services;

public interface IStaffPatientApiClient
{
    Task<PagedResponse<StaffPatientSummaryResponse>> SearchAsync(
        StaffPatientSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<PagedResponse<StaffPatientLookupItemResponse>> LookupAsync(
        StaffPatientLookupRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<StaffPatientDetailResponse> GetByIdAsync(
        Guid patientId,
        Guid? clinicId = null,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<StaffPatientDetailResponse> UpdateClinicProfileAsync(
        Guid patientId,
        UpdateClinicPatientRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicPatientEnrollmentResponse> EnrollAsync(
        Guid clinicId,
        Guid patientId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class StaffPatientApiClient : IStaffPatientApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public StaffPatientApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PagedResponse<StaffPatientSummaryResponse>> SearchAsync(
        StaffPatientSearchRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildSearchQuery("api/v1/staff/patients", request, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>(cancellationToken))
               ?? PagedResponse<StaffPatientSummaryResponse>.Create([], request.Page, request.PageSize, 0);
    }

    public async Task<PagedResponse<StaffPatientLookupItemResponse>> LookupAsync(
        StaffPatientLookupRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildLookupQuery("api/v1/staff/patients/lookup", request, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientLookupItemResponse>>(cancellationToken))
               ?? PagedResponse<StaffPatientLookupItemResponse>.Create([], request.Page, request.PageSize, 0);
    }

    public async Task<StaffPatientDetailResponse> GetByIdAsync(
        Guid patientId,
        Guid? clinicId = null,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendDetailQuery($"api/v1/staff/patients/{patientId:D}", clinicId, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<StaffPatientDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid patient detail response", null, null);
    }

    public async Task<StaffPatientDetailResponse> UpdateClinicProfileAsync(
        Guid patientId,
        UpdateClinicPatientRequest request,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/staff/patients/{patientId:D}/clinic-profile", platformAdminBypass);
        using var response = await client.PatchAsJsonAsync(url, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<StaffPatientDetailResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic-profile update response", null, null);
    }

    public async Task<ClinicPatientEnrollmentResponse> EnrollAsync(
        Guid clinicId,
        Guid patientId,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = AppendBypass($"api/v1/clinics/{clinicId:D}/patients/{patientId:D}/enroll", platformAdminBypass);
        using var response = await client.PostAsync(url, content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<ClinicPatientEnrollmentResponse>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid enrollment response", null, null);
    }

    private static string AppendBypass(string path, bool platformAdminBypass) =>
        platformAdminBypass ? $"{path}?platformAdminBypass=true" : path;

    private static string AppendDetailQuery(string path, Guid? clinicId, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (clinicId is Guid id && id != Guid.Empty)
        {
            parts.Add($"clinicId={id:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

    private static string BuildSearchQuery(string path, StaffPatientSearchRequest request, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"page={request.Page}",
            $"pageSize={request.PageSize}",
            $"sortBy={Uri.EscapeDataString(request.SortBy)}",
            $"sortDirection={Uri.EscapeDataString(request.SortDirection)}",
        };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parts.Add($"search={Uri.EscapeDataString(request.Search)}");
        }

        if (!string.IsNullOrWhiteSpace(request.LocalPatientNumber))
        {
            parts.Add($"localPatientNumber={Uri.EscapeDataString(request.LocalPatientNumber)}");
        }

        if (request.PatientIsActive is bool active)
        {
            parts.Add($"patientIsActive={active.ToString().ToLowerInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(request.ClinicPatientStatus))
        {
            parts.Add($"clinicPatientStatus={Uri.EscapeDataString(request.ClinicPatientStatus)}");
        }

        if (request.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
        }

        if (platformAdminBypass)
        {
            parts.Add("platformAdminBypass=true");
        }

        return $"{path}?{string.Join('&', parts)}";
    }

    private static string BuildLookupQuery(string path, StaffPatientLookupRequest request, bool platformAdminBypass)
    {
        var parts = new List<string>
        {
            $"page={request.Page}",
            $"pageSize={request.PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parts.Add($"search={Uri.EscapeDataString(request.Search)}");
        }

        if (!string.IsNullOrWhiteSpace(request.LocalPatientNumber))
        {
            parts.Add($"localPatientNumber={Uri.EscapeDataString(request.LocalPatientNumber)}");
        }

        if (request.ClinicId is Guid clinicId && clinicId != Guid.Empty)
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
