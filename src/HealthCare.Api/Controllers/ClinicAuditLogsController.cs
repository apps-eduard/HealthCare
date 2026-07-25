using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/clinic/audit-logs")]
public sealed class ClinicAuditLogsController : ControllerBase
{
    private readonly IClinicAuditLogService _auditLogs;

    public ClinicAuditLogsController(IClinicAuditLogService auditLogs)
    {
        _auditLogs = auditLogs;
    }

    [AuthorizePermission(Permissions.Clinics.AuditLogsRead)]
    [HttpGet]
    [ProducesResponseType(typeof(ClinicAuditLogListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicAuditLogListResponse>> Search(
        [FromQuery] ClinicAuditLogQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _auditLogs.SearchAsync(query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.AuditLogsRead)]
    [HttpGet("{eventId:guid}")]
    [ProducesResponseType(typeof(ClinicAuditLogDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicAuditLogDetailResponse>> GetById(
        Guid eventId,
        [FromQuery] ClinicAuditLogQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _auditLogs.GetByIdAsync(eventId, query, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Clinics.AuditLogsRead)]
    [HttpGet("by-correlation/{correlationId}")]
    [ProducesResponseType(typeof(ClinicAuditLogListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicAuditLogListResponse>> GetByCorrelationId(
        string correlationId,
        [FromQuery] ClinicAuditLogQuery query,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _auditLogs.GetByCorrelationIdAsync(correlationId, query, bypass, cancellationToken));
    }
}
