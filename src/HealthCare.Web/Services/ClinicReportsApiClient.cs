using System.Net.Http.Json;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Web.Services;

public interface IClinicReportsApiClient
{
    Task<ClinicAppointmentReportResponse> GetAppointmentsAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicDoctorAppointmentsReportResponse> GetDoctorsAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicPatientEnrollmentReportResponse> GetPatientsAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<ClinicOperationsReportResponse> GetRemindersAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public sealed class ClinicReportsApiClient : IClinicReportsApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClinicReportsApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<ClinicAppointmentReportResponse> GetAppointmentsAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<ClinicAppointmentReportResponse>("api/v1/clinic/reports/appointments", query, platformAdminBypass, cancellationToken);

    public Task<ClinicDoctorAppointmentsReportResponse> GetDoctorsAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<ClinicDoctorAppointmentsReportResponse>("api/v1/clinic/reports/doctors", query, platformAdminBypass, cancellationToken);

    public Task<ClinicPatientEnrollmentReportResponse> GetPatientsAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<ClinicPatientEnrollmentReportResponse>("api/v1/clinic/reports/patients", query, platformAdminBypass, cancellationToken);

    public Task<ClinicOperationsReportResponse> GetRemindersAsync(
        ClinicReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<ClinicOperationsReportResponse>("api/v1/clinic/reports/reminders", query, platformAdminBypass, cancellationToken);

    private async Task<T> GetAsync<T>(
        string path,
        ClinicReportQuery query,
        bool platformAdminBypass,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery(path, query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid clinic report response", null, null);
    }

    private static string BuildQuery(string path, ClinicReportQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.ClinicId is Guid clinicId && clinicId != Guid.Empty)
        {
            parts.Add($"clinicId={clinicId:D}");
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

        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }
}
