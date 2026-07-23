using HealthCare.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

/// <summary>
/// Minimal authenticated probe used to prove tenant isolation without adding business modules.
/// Client-supplied ids are the resource under check — never the caller's identity scope.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/scope-probe")]
public sealed class ScopeProbeController : ControllerBase
{
    private readonly ITenantAccessService _tenantAccess;
    private readonly ICurrentUser _currentUser;

    public ScopeProbeController(ITenantAccessService tenantAccess, ICurrentUser currentUser)
    {
        _tenantAccess = tenantAccess;
        _currentUser = currentUser;
    }

    [HttpGet("organization")]
    public IActionResult CheckOrganization(
        [FromQuery] Guid organizationId,
        [FromQuery] bool platformAdminBypass = false)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        _tenantAccess.EnsureCanAccessOrganization(organizationId, bypass);
        return Ok(new
        {
            allowed = true,
            resolvedOrganizationId = _currentUser.OrganizationId,
            requestedOrganizationId = organizationId,
        });
    }

    [HttpGet("clinic")]
    public IActionResult CheckClinic(
        [FromQuery] Guid clinicId,
        [FromQuery] bool platformAdminBypass = false)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        _tenantAccess.EnsureCanAccessClinic(clinicId, bypass);
        return Ok(new
        {
            allowed = true,
            resolvedClinicId = _currentUser.ClinicId,
            requestedClinicId = clinicId,
        });
    }

    [HttpGet("patient")]
    public IActionResult CheckPatient(
        [FromQuery] Guid patientId,
        [FromQuery] bool platformAdminBypass = false)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        _tenantAccess.EnsureCanAccessPatient(patientId, bypass);
        return Ok(new
        {
            allowed = true,
            resolvedPatientId = _currentUser.PatientId,
            requestedPatientId = patientId,
        });
    }
}
