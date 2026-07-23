using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize]
[Route("api/v1/patients")]
public sealed class PatientsController : ControllerBase
{
    private readonly IPatientService _patientService;

    public PatientsController(IPatientService patientService)
    {
        _patientService = patientService;
    }

    /// <summary>
    /// Returns the authenticated patient's own profile using the server-resolved PatientId.
    /// Client-supplied PatientId values are ignored.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.PatientSelfScope)]
    [HttpGet("me")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PatientProfileResponse>> GetMe(CancellationToken cancellationToken)
    {
        var profile = await _patientService.GetCurrentPatientProfileAsync(cancellationToken);
        return Ok(profile);
    }

    /// <summary>
    /// Returns a patient profile when the caller is authorized (self, clinic/org staff, or explicit platform bypass).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.Authenticated)]
    [HttpGet("{patientId:guid}")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PatientProfileResponse>> GetById(
        Guid patientId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var profile = await _patientService.GetPatientByIdAsync(patientId, bypass, cancellationToken);
        return Ok(profile);
    }
}
