using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/clinic/settings")]
public sealed class ClinicSettingsController : ControllerBase
{
    private readonly IClinicSettingsService _settings;

    public ClinicSettingsController(IClinicSettingsService settings)
    {
        _settings = settings;
    }

    [AuthorizePermission(Permissions.Clinics.ProfileRead)]
    [HttpGet]
    [ProducesResponseType(typeof(ClinicSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClinicSettingsResponse>> Get(
        [FromQuery] ClinicSettingsQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _settings.GetAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.ProfileUpdate)]
    [HttpPatch]
    [ProducesResponseType(typeof(ClinicSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicSettingsResponse>> Update(
        [FromBody] UpdateClinicSettingsRequest request,
        [FromQuery] ClinicSettingsQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _settings.UpdateAsync(request, query, bypass, cancellationToken));
    }
}
