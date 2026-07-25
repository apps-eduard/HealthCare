using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Doctors;
using HealthCare.Contracts.Doctors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/doctor/profile")]
public sealed class DoctorProfileController : ControllerBase
{
    private readonly IDoctorProfileService _profile;

    public DoctorProfileController(IDoctorProfileService profile)
    {
        _profile = profile;
    }

    [AuthorizePermission(Permissions.Doctors.ProfileRead)]
    [HttpGet]
    [ProducesResponseType(typeof(DoctorProfileResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DoctorProfileResponse>> Get(
        [FromQuery] DoctorProfileQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _profile.GetAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Doctors.ProfileUpdate)]
    [HttpPatch]
    [ProducesResponseType(typeof(DoctorProfileResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DoctorProfileResponse>> Patch(
        [FromBody] UpdateDoctorProfileRequest request,
        [FromQuery] DoctorProfileQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _profile.UpdateAsync(request, query, bypass, cancellationToken));
    }
}
