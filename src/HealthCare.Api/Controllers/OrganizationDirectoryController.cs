using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

/// <summary>
/// Platform-only organization directory. Listing does not grant tenant resource access.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/platform/organizations")]
public sealed class OrganizationDirectoryController : ControllerBase
{
    private readonly IOrganizationDirectoryService _organizations;

    public OrganizationDirectoryController(IOrganizationDirectoryService organizations)
    {
        _organizations = organizations;
    }

    [AuthorizePermission(Permissions.Organizations.Read)]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrganizationDirectoryItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrganizationDirectoryItemResponse>>> Search(
        [FromQuery] OrganizationSearchRequest request,
        CancellationToken cancellationToken = default) =>
        Ok(await _organizations.SearchAsync(request, cancellationToken));

    [AuthorizePermission(Permissions.Organizations.Read)]
    [HttpGet("{organizationId:guid}")]
    [ProducesResponseType(typeof(OrganizationDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationDetailResponse>> GetById(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        Ok(await _organizations.GetByIdAsync(organizationId, cancellationToken));
}
