using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/organization/dashboard")]
public sealed class OrganizationDashboardController : ControllerBase
{
    private readonly IOrganizationDashboardService _dashboard;

    public OrganizationDashboardController(IOrganizationDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [AuthorizePermission(Permissions.Organizations.DashboardRead)]
    [HttpGet]
    [ProducesResponseType(typeof(OrganizationDashboardResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDashboardResponse>> Get(
        [FromQuery] OrganizationDashboardQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _dashboard.GetAsync(query, bypass, cancellationToken));
    }
}
