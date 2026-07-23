using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

/// <summary>
/// Staff clinic-scoped patient search and clinic-profile administration.
/// Tenant rules are enforced in <see cref="IStaffPatientService"/>, not in this controller.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.StaffUser)]
[Route("api/v1/staff/patients")]
public sealed class StaffPatientsController : ControllerBase
{
    private readonly IStaffPatientService _staffPatientService;

    public StaffPatientsController(IStaffPatientService staffPatientService)
    {
        _staffPatientService = staffPatientService;
    }

    [AuthorizePermission(Permissions.Patients.Search)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<StaffPatientSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<StaffPatientSummaryResponse>>> Search(
        [FromQuery] StaffPatientSearchRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _staffPatientService.SearchAsync(request, bypass, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.Patients.Read)]
    [HttpGet("{patientId:guid}")]
    [ProducesResponseType(typeof(StaffPatientDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StaffPatientDetailResponse>> GetByPatientId(
        Guid patientId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _staffPatientService.GetByPatientIdAsync(patientId, bypass, cancellationToken);
        return Ok(result);
    }

    [AuthorizePermission(Permissions.Patients.UpdateClinicStatus)]
    [HttpPatch("{patientId:guid}/clinic-profile")]
    [ProducesResponseType(typeof(StaffPatientDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffPatientDetailResponse>> UpdateClinicProfile(
        Guid patientId,
        [FromBody] UpdateClinicPatientRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _staffPatientService.UpdateClinicProfileAsync(patientId, request, bypass, cancellationToken);
        return Ok(result);
    }
}
