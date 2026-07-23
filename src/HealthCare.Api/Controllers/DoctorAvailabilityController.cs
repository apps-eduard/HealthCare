using HealthCare.Api.Authorization;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize]
[Route("api/v1")]
public sealed class DoctorAvailabilityController : ControllerBase
{
    private readonly IDoctorDirectoryService _directory;
    private readonly IDoctorAvailabilityService _availability;
    private readonly IAppointmentSlotService _slots;

    public DoctorAvailabilityController(
        IDoctorDirectoryService directory,
        IDoctorAvailabilityService availability,
        IAppointmentSlotService slots)
    {
        _directory = directory;
        _availability = availability;
        _slots = slots;
    }

    [AuthorizePermission(Permissions.Availability.Read)]
    [HttpGet("clinics/{clinicCode}/doctors")]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicDoctorResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ClinicDoctorResponse>>> ListDoctors(
        string clinicCode,
        CancellationToken cancellationToken)
    {
        var result = await _directory.ListDoctorsByClinicCodeAsync(clinicCode, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.Availability.Read)]
    [HttpGet("clinics/{clinicCode}/doctors/{staffMemberId:guid}/available-slots")]
    [ProducesResponseType(typeof(IReadOnlyList<AvailableSlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IReadOnlyList<AvailableSlotResponse>>> GetAvailableSlots(
        string clinicCode,
        Guid staffMemberId,
        [FromQuery] DateOnly date,
        [FromQuery] int? durationMinutes,
        CancellationToken cancellationToken)
    {
        var result = await _slots.GetAvailableSlotsAsync(
            clinicCode,
            staffMemberId,
            new AvailableSlotsQuery { Date = date, DurationMinutes = durationMinutes },
            cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizeAnyPermission(
        Permissions.Availability.ManageSelf,
        Permissions.Availability.ManageClinic,
        Permissions.Availability.ManageOrganization)]
    [HttpGet("staff/doctors/{staffMemberId:guid}/availability")]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorAvailabilityResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DoctorAvailabilityResponse>>> ListAvailability(
        Guid staffMemberId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _availability.ListAvailabilityAsync(staffMemberId, bypass, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizeAnyPermission(
        Permissions.Availability.ManageSelf,
        Permissions.Availability.ManageClinic,
        Permissions.Availability.ManageOrganization)]
    [HttpGet("staff/doctors/{staffMemberId:guid}/availability-exceptions")]
    [ProducesResponseType(typeof(IReadOnlyList<DoctorAvailabilityExceptionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DoctorAvailabilityExceptionResponse>>> ListExceptions(
        Guid staffMemberId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _availability.ListExceptionsAsync(staffMemberId, bypass, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizeAnyPermission(
        Permissions.Availability.ManageSelf,
        Permissions.Availability.ManageClinic,
        Permissions.Availability.ManageOrganization)]
    [HttpPost("staff/doctors/{staffMemberId:guid}/availability")]
    [ProducesResponseType(typeof(DoctorAvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorAvailabilityResponse>> CreateAvailability(
        Guid staffMemberId,
        [FromBody] CreateDoctorAvailabilityRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _availability.CreateAvailabilityAsync(staffMemberId, request, bypass, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizeAnyPermission(
        Permissions.Availability.ManageSelf,
        Permissions.Availability.ManageClinic,
        Permissions.Availability.ManageOrganization)]
    [HttpPatch("staff/doctors/{staffMemberId:guid}/availability/{availabilityId:guid}")]
    [ProducesResponseType(typeof(DoctorAvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorAvailabilityResponse>> UpdateAvailability(
        Guid staffMemberId,
        Guid availabilityId,
        [FromBody] UpdateDoctorAvailabilityRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _availability.UpdateAvailabilityAsync(
            staffMemberId,
            availabilityId,
            request,
            bypass,
            cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizeAnyPermission(
        Permissions.Availability.ManageSelf,
        Permissions.Availability.ManageClinic,
        Permissions.Availability.ManageOrganization)]
    [HttpDelete("staff/doctors/{staffMemberId:guid}/availability/{availabilityId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAvailability(
        Guid staffMemberId,
        Guid availabilityId,
        [FromQuery] int expectedVersion,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        await _availability.DeleteAvailabilityAsync(
            staffMemberId,
            availabilityId,
            expectedVersion,
            bypass,
            cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizeAnyPermission(
        Permissions.Availability.ManageSelf,
        Permissions.Availability.ManageClinic,
        Permissions.Availability.ManageOrganization)]
    [HttpPost("staff/doctors/{staffMemberId:guid}/availability-exceptions")]
    [ProducesResponseType(typeof(DoctorAvailabilityExceptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DoctorAvailabilityExceptionResponse>> CreateException(
        Guid staffMemberId,
        [FromBody] CreateDoctorAvailabilityExceptionRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _availability.CreateExceptionAsync(staffMemberId, request, bypass, cancellationToken);
        return Ok(result);
    }

    [Authorize(Policy = AuthorizationPolicies.StaffUser)]
    [AuthorizeAnyPermission(
        Permissions.Availability.ManageSelf,
        Permissions.Availability.ManageClinic,
        Permissions.Availability.ManageOrganization)]
    [HttpDelete("staff/doctors/{staffMemberId:guid}/availability-exceptions/{exceptionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteException(
        Guid staffMemberId,
        Guid exceptionId,
        [FromQuery] int expectedVersion,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        await _availability.DeleteExceptionAsync(
            staffMemberId,
            exceptionId,
            expectedVersion,
            bypass,
            cancellationToken);
        return NoContent();
    }
}
