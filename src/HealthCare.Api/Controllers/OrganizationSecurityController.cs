using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Security;
using HealthCare.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/organization/security")]
public sealed class OrganizationSecurityController : ControllerBase
{
    private readonly IOrganizationSecurityService _security;

    public OrganizationSecurityController(IOrganizationSecurityService security)
    {
        _security = security;
    }

    [AuthorizePermission(Permissions.SecuritySessions.Read)]
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(OrganizationSecuritySessionListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationSecuritySessionListResponse>> ListSessions(
        [FromQuery] OrganizationSecurityQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _security.ListSessionsAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.SecuritySessions.Revoke)]
    [HttpPost("staff/{staffMemberId:guid}/sessions/revoke")]
    [ProducesResponseType(typeof(RevokeOrganizationSessionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RevokeOrganizationSessionsResponse>> RevokeStaffSessions(
        Guid staffMemberId,
        [FromBody] RevokeOrganizationSessionsRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _security.RevokeStaffSessionsAsync(staffMemberId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.SecuritySessions.Revoke)]
    [HttpPost("staff/{staffMemberId:guid}/compromise-response")]
    [ProducesResponseType(typeof(CompromisedAccountResponseResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<CompromisedAccountResponseResult>> CompromiseResponse(
        Guid staffMemberId,
        [FromBody] CompromisedAccountResponseRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _security.RespondToCompromisedAccountAsync(staffMemberId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.SecuritySessions.Read)]
    [HttpGet("failed-logins")]
    [ProducesResponseType(typeof(OrganizationFailedLoginSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationFailedLoginSummaryResponse>> FailedLogins(
        [FromQuery] OrganizationSecurityQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _security.GetFailedLoginSummaryAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.SecuritySessions.Read)]
    [HttpGet("authorization-denials")]
    [ProducesResponseType(typeof(OrganizationSecurityEventSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationSecurityEventSummaryResponse>> AuthorizationDenials(
        [FromQuery] OrganizationSecurityQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _security.GetAuthorizationDenialSummaryAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.SecuritySessions.Read)]
    [HttpGet("cross-clinic-attempts")]
    [ProducesResponseType(typeof(OrganizationSecurityEventSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationSecurityEventSummaryResponse>> CrossClinicAttempts(
        [FromQuery] OrganizationSecurityQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _security.GetCrossClinicAttemptSummaryAsync(query, bypass, cancellationToken));
    }
}
