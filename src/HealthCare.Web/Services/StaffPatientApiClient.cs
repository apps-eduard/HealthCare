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
        var url = BuildQuery("api/v1/staff/patients", request, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>(cancellationToken))
               ?? PagedResponse<StaffPatientSummaryResponse>.Create([], request.Page, request.PageSize, 0);
    }

    private static string BuildQuery(string path, StaffPatientSearchRequest request, bool platformAdminBypass)
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
}
