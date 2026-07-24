using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/clinic/dashboard")]
public sealed class ClinicDashboardController : ControllerBase
{
    private readonly IClinicDashboardService _dashboard;

    public ClinicDashboardController(IClinicDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [AuthorizePermission(Permissions.Clinics.DashboardRead)]
    [HttpGet]
    [ProducesResponseType(typeof(ClinicDashboardResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicDashboardResponse>> Get(
        [FromQuery] ClinicDashboardQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _dashboard.GetAsync(query, bypass, cancellationToken));
    }
}
