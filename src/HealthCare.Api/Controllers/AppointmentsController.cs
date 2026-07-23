using HealthCare.Api.Authorization;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize]
[Route("api/v1")]
public sealed class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointments;

    public AppointmentsController(IAppointmentService appointments)
    {
        _appointments = appointments;
    }

    [Authorize(Policy = AuthorizationPolicies.PatientSelfScope)]
    [AuthorizePermission(Permissions.Appointments.Create)]
    [HttpPost("patients/me/appointments")]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentResponse>> CreateForPatient(
        [FromBody] CreatePatientAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _appointments.CreateForCurrentPatientAsync(request, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.PatientSelfScope)]
    [AuthorizePermission(Permissions.Appointments.Read)]
    [HttpGet("patients/me/appointments")]
    [ProducesResponseType(typeof(PagedResponse<AppointmentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AppointmentResponse>>> ListForPatient(
        [FromQuery] AppointmentListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _appointments.ListForCurrentPatientAsync(query, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizePermission(Permissions.Appointments.Create)]
    [HttpPost("staff/appointments")]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentResponse>> CreateForStaff(
        [FromBody] CreateStaffAppointmentRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _appointments.CreateForStaffAsync(request, bypass, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizePermission(Permissions.Appointments.Read)]
    [HttpGet("staff/appointments")]
    [ProducesResponseType(typeof(PagedResponse<AppointmentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AppointmentResponse>>> ListForStaff(
        [FromQuery] AppointmentListQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _appointments.ListForStaffAsync(query, bypass, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.Appointments.Read)]
    [HttpGet("appointments/{appointmentId:guid}")]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppointmentResponse>> GetById(
        Guid appointmentId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _appointments.GetByIdAsync(appointmentId, bypass, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizePermission(Permissions.Appointments.Confirm)]
    [HttpPost("staff/appointments/{appointmentId:guid}/confirm")]
    public async Task<ActionResult<AppointmentResponse>> Confirm(
        Guid appointmentId,
        [FromBody] AppointmentActionRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _appointments.ConfirmAsync(appointmentId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Appointments.Cancel)]
    [HttpPost("appointments/{appointmentId:guid}/cancel")]
    public async Task<ActionResult<AppointmentResponse>> Cancel(
        Guid appointmentId,
        [FromBody] AppointmentActionRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _appointments.CancelAsync(appointmentId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Appointments.Reschedule)]
    [HttpPost("appointments/{appointmentId:guid}/reschedule")]
    [ProducesResponseType(typeof(AppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AppointmentResponse>> Reschedule(
        Guid appointmentId,
        [FromBody] RescheduleAppointmentRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _appointments.RescheduleAsync(appointmentId, request, bypass, cancellationToken));
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizePermission(Permissions.Appointments.CheckIn)]
    [HttpPost("staff/appointments/{appointmentId:guid}/check-in")]
    public async Task<ActionResult<AppointmentResponse>> CheckIn(
        Guid appointmentId,
        [FromBody] AppointmentActionRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _appointments.CheckInAsync(appointmentId, request, bypass, cancellationToken));
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizePermission(Permissions.Appointments.Complete)]
    [HttpPost("staff/appointments/{appointmentId:guid}/complete")]
    public async Task<ActionResult<AppointmentResponse>> Complete(
        Guid appointmentId,
        [FromBody] AppointmentActionRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _appointments.CompleteAsync(appointmentId, request, bypass, cancellationToken));
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizePermission(Permissions.Appointments.NoShow)]
    [HttpPost("staff/appointments/{appointmentId:guid}/no-show")]
    public async Task<ActionResult<AppointmentResponse>> NoShow(
        Guid appointmentId,
        [FromBody] AppointmentActionRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _appointments.MarkNoShowAsync(appointmentId, request, bypass, cancellationToken));
    }
}
