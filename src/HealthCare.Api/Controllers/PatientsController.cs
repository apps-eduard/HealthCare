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
    private readonly IPatientClinicRegistrationService _clinicRegistration;

    public PatientsController(
        IPatientService patientService,
        IPatientClinicRegistrationService clinicRegistration)
    {
        _patientService = patientService;
        _clinicRegistration = clinicRegistration;
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
    /// Partially updates the authenticated patient's own profile.
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.PatientSelfScope)]
    [HttpPatch("me")]
    [ProducesResponseType(typeof(PatientProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientProfileResponse>> PatchMe(
        [FromBody] UpdatePatientProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await _patientService.UpdateCurrentPatientProfileAsync(request, cancellationToken);
        return Ok(profile);
    }

    /// <summary>
    /// Registers the authenticated patient with a clinic using a trusted public clinic code (slug).
    /// </summary>
    [Authorize(Policy = AuthorizationPolicies.PatientSelfScope)]
    [HttpPost("me/clinics/register")]
    [ProducesResponseType(typeof(ClinicPatientEnrollmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClinicPatientEnrollmentResponse>> RegisterWithClinic(
        [FromBody] RegisterPatientWithClinicRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _clinicRegistration.RegisterCurrentPatientWithClinicAsync(request, cancellationToken);
        return Ok(result);
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
