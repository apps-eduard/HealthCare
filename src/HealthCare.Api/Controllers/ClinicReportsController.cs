using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/clinic/reports")]
public sealed class ClinicReportsController : ControllerBase
{
    private readonly IClinicReportsService _reports;

    public ClinicReportsController(IClinicReportsService reports)
    {
        _reports = reports;
    }

    [AuthorizePermission(Permissions.Clinics.ReportsRead)]
    [HttpGet("appointments")]
    [ProducesResponseType(typeof(ClinicAppointmentReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicAppointmentReportResponse>> GetAppointments(
        [FromQuery] ClinicReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetAppointmentsAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.ReportsRead)]
    [HttpGet("doctors")]
    [ProducesResponseType(typeof(ClinicDoctorAppointmentsReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicDoctorAppointmentsReportResponse>> GetDoctors(
        [FromQuery] ClinicReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetDoctorsAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.ReportsRead)]
    [HttpGet("patients")]
    [ProducesResponseType(typeof(ClinicPatientEnrollmentReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicPatientEnrollmentReportResponse>> GetPatients(
        [FromQuery] ClinicReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetPatientsAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.ReportsRead)]
    [HttpGet("reminders")]
    [ProducesResponseType(typeof(ClinicOperationsReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicOperationsReportResponse>> GetReminders(
        [FromQuery] ClinicReportQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _reports.GetRemindersAsync(query, bypass, cancellationToken));
    }
}
