using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/organization/audit-logs")]
public sealed class OrganizationAuditLogsController : ControllerBase
{
    private readonly IOrganizationAuditLogService _auditLogs;

    public OrganizationAuditLogsController(IOrganizationAuditLogService auditLogs)
    {
        _auditLogs = auditLogs;
    }

    [AuthorizePermission(Permissions.Organizations.AuditLogsRead)]
    [HttpGet]
    [ProducesResponseType(typeof(OrganizationAuditLogListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationAuditLogListResponse>> Search(
        [FromQuery] OrganizationAuditLogQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _auditLogs.SearchAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.AuditLogsRead)]
    [HttpGet("{eventId:guid}")]
    [ProducesResponseType(typeof(OrganizationAuditLogDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationAuditLogDetailResponse>> GetById(
        Guid eventId,
        [FromQuery] OrganizationAuditLogQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _auditLogs.GetByIdAsync(eventId, query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Organizations.AuditLogsRead)]
    [HttpGet("by-correlation/{correlationId}")]
    [ProducesResponseType(typeof(OrganizationAuditLogListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrganizationAuditLogListResponse>> GetByCorrelationId(
        string correlationId,
        [FromQuery] OrganizationAuditLogQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _auditLogs.GetByCorrelationIdAsync(correlationId, query, bypass, cancellationToken));
    }
}
