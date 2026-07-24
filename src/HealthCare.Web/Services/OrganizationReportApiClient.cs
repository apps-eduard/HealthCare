using System.Net.Http.Headers;
using System.Net.Http.Json;
using HealthCare.Contracts.Organizations;
using Microsoft.JSInterop;

namespace HealthCare.Web.Services;

public sealed class DownloadedFile
{
    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required byte[] Content { get; init; }
}

public interface IOrganizationReportApiClient
{
    Task<OrganizationAppointmentReportResponse> GetAppointmentsAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationStaffReportResponse> GetStaffAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationPatientReportResponse> GetPatientsAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationAvailabilityReportResponse> GetAvailabilityAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationReminderFailureReportResponse> GetReminderFailuresAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<OrganizationSummaryFailureReportResponse> GetSummaryFailuresAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);

    Task<DownloadedFile> ExportCsvAsync(
        string reportType,
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default);
}

public interface IBrowserFileDownload
{
    Task DownloadAsync(DownloadedFile file, CancellationToken cancellationToken = default);
}

public sealed class BrowserFileDownload : IBrowserFileDownload
{
    private readonly IJSRuntime _js;

    public BrowserFileDownload(IJSRuntime js)
    {
        _js = js;
    }

    public Task DownloadAsync(DownloadedFile file, CancellationToken cancellationToken = default)
    {
        var base64 = Convert.ToBase64String(file.Content);
        return _js.InvokeVoidAsync(
            "healthcareShell.downloadBase64File",
            cancellationToken,
            file.FileName,
            file.ContentType,
            base64).AsTask();
    }
}

public sealed class OrganizationReportApiClient : IOrganizationReportApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public OrganizationReportApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<OrganizationAppointmentReportResponse> GetAppointmentsAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationAppointmentReportResponse>("appointments", query, platformAdminBypass, cancellationToken);

    public Task<OrganizationStaffReportResponse> GetStaffAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationStaffReportResponse>("staff", query, platformAdminBypass, cancellationToken);

    public Task<OrganizationPatientReportResponse> GetPatientsAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationPatientReportResponse>("patients", query, platformAdminBypass, cancellationToken);

    public Task<OrganizationAvailabilityReportResponse> GetAvailabilityAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationAvailabilityReportResponse>("availability", query, platformAdminBypass, cancellationToken);

    public Task<OrganizationReminderFailureReportResponse> GetReminderFailuresAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationReminderFailureReportResponse>("reminder-failures", query, platformAdminBypass, cancellationToken);

    public Task<OrganizationSummaryFailureReportResponse> GetSummaryFailuresAsync(
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<OrganizationSummaryFailureReportResponse>("summary-failures", query, platformAdminBypass, cancellationToken);

    public async Task<DownloadedFile> ExportCsvAsync(
        string reportType,
        OrganizationReportQuery query,
        bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(reportType.Trim());
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery($"api/v1/organization/reports/{encoded}/export.csv", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var fileName = ParseFileName(response.Content.Headers.ContentDisposition)
                       ?? $"organization-report-{reportType}.csv";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "text/csv";
        return new DownloadedFile
        {
            FileName = fileName,
            ContentType = contentType,
            Content = bytes,
        };
    }

    private async Task<T> GetAsync<T>(
        string reportType,
        OrganizationReportQuery query,
        bool platformAdminBypass,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("HealthCareApi");
        var url = BuildQuery($"api/v1/organization/reports/{reportType}", query, platformAdminBypass);
        using var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await ApiProblemException.FromResponseAsync(response, cancellationToken);
        }

        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))
               ?? throw new ApiProblemException(500, "Invalid organization report response", null, null);
    }

    private static string? ParseFileName(ContentDispositionHeaderValue? disposition)
    {
        if (disposition is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(disposition.FileNameStar))
        {
            return disposition.FileNameStar.Trim('"');
        }

        if (!string.IsNullOrWhiteSpace(disposition.FileName))
        {
            return disposition.FileName.Trim('"');
        }

        return null;
    }

    private static string BuildQuery(string path, OrganizationReportQuery query, bool platformAdminBypass)
    {
        var parts = new List<string>();
        if (query.OrganizationId is Guid orgId && orgId != Guid.Empty)
        {
            parts.Add($"organizationId={orgId:D}");
        }

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
