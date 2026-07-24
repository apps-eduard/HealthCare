using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/organization/clinics")]
public sealed class OrganizationClinicsController : ControllerBase
{
    private readonly IClinicManagementService _clinics;

    public OrganizationClinicsController(IClinicManagementService clinics)
    {
        _clinics = clinics;
    }

    [AuthorizePermission(Permissions.Clinics.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrganizationClinicListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrganizationClinicListItemResponse>>> Search(
        [FromQuery] OrganizationClinicSearchRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _clinics.SearchAsync(request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.Read)]
    [HttpGet("{clinicId:guid}")]
    [ProducesResponseType(typeof(OrganizationClinicDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationClinicDetailResponse>> GetById(
        Guid clinicId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _clinics.GetByIdAsync(clinicId, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.Create)]
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationClinicDetailResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrganizationClinicDetailResponse>> Create(
        [FromBody] CreateOrganizationClinicRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        var created = await _clinics.CreateAsync(request, bypass, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { clinicId = created.ClinicId }, created);
    }

    [AuthorizePermission(Permissions.Clinics.Update)]
    [HttpPatch("{clinicId:guid}")]
    [ProducesResponseType(typeof(OrganizationClinicDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationClinicDetailResponse>> Update(
        Guid clinicId,
        [FromBody] UpdateOrganizationClinicRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _clinics.UpdateAsync(clinicId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.Activate)]
    [HttpPost("{clinicId:guid}/activate")]
    [ProducesResponseType(typeof(OrganizationClinicDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationClinicDetailResponse>> Activate(
        Guid clinicId,
        [FromBody] ClinicActivationRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _clinics.ActivateAsync(clinicId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.Deactivate)]
    [HttpPost("{clinicId:guid}/deactivate")]
    [ProducesResponseType(typeof(OrganizationClinicDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationClinicDetailResponse>> Deactivate(
        Guid clinicId,
        [FromBody] ClinicActivationRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _clinics.DeactivateAsync(clinicId, request, bypass, cancellationToken));
    }
}
