using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.StaffUser)]
[Route("api/v1/staff/appointments/{appointmentId:guid}/reminders")]
public sealed class AppointmentRemindersController : ControllerBase
{
    private readonly IAppointmentReminderService _reminders;

    public AppointmentRemindersController(IAppointmentReminderService reminders)
    {
        _reminders = reminders;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AppointmentReminderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AppointmentReminderResponse>>> List(
        Guid appointmentId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _reminders.ListForAppointmentAsync(appointmentId, bypass, cancellationToken);
        return Ok(result);
    }

    [HttpPost("retry")]
    [ProducesResponseType(typeof(AppointmentReminderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppointmentReminderResponse>> Retry(
        Guid appointmentId,
        [FromBody] RetryAppointmentReminderRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _reminders.RetryAsync(appointmentId, request.ReminderId, bypass, cancellationToken);
        return Ok(result);
    }
}
