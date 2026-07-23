using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.StaffUser)]
[Route("api/v1/clinics")]
public sealed class ClinicsController : ControllerBase
{
    private readonly IClinicEnrollmentService _enrollmentService;

    public ClinicsController(IClinicEnrollmentService enrollmentService)
    {
        _enrollmentService = enrollmentService;
    }

    [HttpPost("{clinicId:guid}/patients/{patientId:guid}/enroll")]
    [ProducesResponseType(typeof(ClinicPatientEnrollmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ClinicPatientEnrollmentResponse>> EnrollPatient(
        Guid clinicId,
        Guid patientId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var result = await _enrollmentService.EnrollAsync(clinicId, patientId, bypass, cancellationToken);
        return Ok(result);
    }
}
