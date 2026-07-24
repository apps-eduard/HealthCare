using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/organization/usage")]
public sealed class OrganizationUsageController : ControllerBase
{
    private readonly IOrganizationUsageService _usage;

    public OrganizationUsageController(IOrganizationUsageService usage)
    {
        _usage = usage;
    }

    [AuthorizePermission(Permissions.Organizations.UsageRead)]
    [HttpGet]
    [ProducesResponseType(typeof(OrganizationUsageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationUsageResponse>> GetUsage(
        [FromQuery] OrganizationUsageQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _usage.GetUsageAsync(query, bypass, cancellationToken));
    }
}
