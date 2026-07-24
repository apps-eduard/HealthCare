using HealthCare.Api.Authorization;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.StaffUser)]
[Route("api/v1/staff")]
public sealed class ClinicAppointmentSummariesController : ControllerBase
{
    private readonly IClinicAppointmentSummaryService _summaries;
    private readonly IStaffOperationsHealthService _operationsHealth;

    public ClinicAppointmentSummariesController(
        IClinicAppointmentSummaryService summaries,
        IStaffOperationsHealthService operationsHealth)
    {
        _summaries = summaries;
        _operationsHealth = operationsHealth;
    }

    [AuthorizePermission(Permissions.Summaries.Read)]
    [HttpGet("clinics/current/appointment-summary")]
    [ProducesResponseType(typeof(ClinicAppointmentSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClinicAppointmentSummaryResponse>> GetCurrent(
        [FromQuery] ClinicAppointmentSummaryQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _summaries.GetForStaffAsync(query, bypass, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.Summaries.Read)]
    [HttpGet("appointment-summary-runs")]
    [ProducesResponseType(typeof(PagedResponse<ClinicAppointmentSummaryRunResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ClinicAppointmentSummaryRunResponse>>> ListRuns(
        [FromQuery] ClinicAppointmentSummaryRunQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _summaries.ListRunsForStaffAsync(query, bypass, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.Summaries.Retry)]
    [HttpPost("clinics/{clinicId:guid}/appointment-summary/{date}/retry")]
    [ProducesResponseType(typeof(ClinicAppointmentSummaryRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicAppointmentSummaryRunResponse>> Retry(
        Guid clinicId,
        string date,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        if (!DateOnly.TryParse(date, out var summaryDate))
        {
            throw AppointmentSummaryException.InvalidDate();
        }

        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _summaries.RetryAsync(clinicId, summaryDate, bypass, cancellationToken);
        return Ok(result);
    }

    [AuthorizeAnyPermission(Permissions.Reminders.Read, Permissions.Summaries.Read)]
    [HttpGet("operations/health")]
    [ProducesResponseType(typeof(StaffOperationsHealthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffOperationsHealthResponse>> GetOperationsHealth(
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _operationsHealth.GetHealthAsync(bypass, cancellationToken);
        return Ok(result);
    }
}
