using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/organization/reports")]
public sealed class OrganizationReportsController : ControllerBase
{
    private readonly IOrganizationReportService _reports;

    public OrganizationReportsController(IOrganizationReportService reports)
    {
        _reports = reports;
    }

    [AuthorizePermission(Permissions.Organizations.ReportsRead)]
    [HttpGet("appointments")]
    [ProducesResponseType(typeof(OrganizationAppointmentReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationAppointmentReportResponse>> GetAppointments(
        [FromQuery] OrganizationReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetAppointmentsAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.ReportsRead)]
    [HttpGet("staff")]
    [ProducesResponseType(typeof(OrganizationStaffReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationStaffReportResponse>> GetStaff(
        [FromQuery] OrganizationReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetStaffAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.ReportsRead)]
    [HttpGet("patients")]
    [ProducesResponseType(typeof(OrganizationPatientReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationPatientReportResponse>> GetPatients(
        [FromQuery] OrganizationReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetPatientsAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.ReportsRead)]
    [HttpGet("availability")]
    [ProducesResponseType(typeof(OrganizationAvailabilityReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationAvailabilityReportResponse>> GetAvailability(
        [FromQuery] OrganizationReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetAvailabilityAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.ReportsRead)]
    [HttpGet("reminder-failures")]
    [ProducesResponseType(typeof(OrganizationReminderFailureReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationReminderFailureReportResponse>> GetReminderFailures(
        [FromQuery] OrganizationReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetReminderFailuresAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.ReportsRead)]
    [HttpGet("summary-failures")]
    [ProducesResponseType(typeof(OrganizationSummaryFailureReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationSummaryFailureReportResponse>> GetSummaryFailures(
        [FromQuery] OrganizationReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetSummaryFailuresAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.ReportsRead)]
    [HttpGet("{reportType}/export.csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportCsv(
        string reportType,
        [FromQuery] OrganizationReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var csv = await _reports.ExportCsvAsync(reportType, query, bypass, cancellationToken);
        return File(csv.Content, csv.ContentType, csv.FileName);
    }
}
