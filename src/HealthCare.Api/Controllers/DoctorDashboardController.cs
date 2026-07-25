using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Doctors;
using HealthCare.Contracts.Doctors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/doctor/dashboard")]
public sealed class DoctorDashboardController : ControllerBase
{
    private readonly IDoctorDashboardService _dashboard;

    public DoctorDashboardController(IDoctorDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [AuthorizePermission(Permissions.Doctors.DashboardRead)]
    [HttpGet]
    [ProducesResponseType(typeof(DoctorDashboardResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DoctorDashboardResponse>> Get(
        [FromQuery] DoctorDashboardQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _dashboard.GetAsync(query, bypass, cancellationToken));
    }
}
