using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/staff-management/clinics")]
public sealed class ClinicDirectoryController : ControllerBase
{
    private readonly IClinicDirectoryService _clinics;

    public ClinicDirectoryController(IClinicDirectoryService clinics)
    {
        _clinics = clinics;
    }

    [AuthorizePermission(Permissions.Clinics.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ClinicDirectoryItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ClinicDirectoryItemResponse>>> Search(
        [FromQuery] ClinicSearchRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _clinics.SearchAsync(request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.Read)]
    [HttpGet("{clinicId:guid}")]
    [ProducesResponseType(typeof(ClinicDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicDetailResponse>> GetById(
        Guid clinicId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _clinics.GetByIdAsync(clinicId, bypass, cancellationToken));
    }
}
