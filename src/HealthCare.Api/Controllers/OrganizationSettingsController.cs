using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/organization/settings")]
public sealed class OrganizationSettingsController : ControllerBase
{
    private readonly IOrganizationSettingsService _settings;

    public OrganizationSettingsController(IOrganizationSettingsService settings)
    {
        _settings = settings;
    }

    [AuthorizePermission(Permissions.Organizations.ProfileRead)]
    [HttpGet]
    [ProducesResponseType(typeof(OrganizationSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationSettingsResponse>> Get(
        [FromQuery] OrganizationSettingsQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _settings.GetAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.ProfileUpdate)]
    [HttpPatch]
    [ProducesResponseType(typeof(OrganizationSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationSettingsResponse>> Update(
        [FromBody] UpdateOrganizationSettingsRequest request,
        [FromQuery] OrganizationSettingsQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _settings.UpdateAsync(request, query, bypass, cancellationToken));
    }
}
