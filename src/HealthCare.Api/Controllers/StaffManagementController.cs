using HealthCare.Api.Authorization;
using HealthCare.Application.Authorization;
using HealthCare.Application.Staff;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthCare.Api.Controllers;

// Authenticated (not StaffUser): PLATFORM_ADMIN may lack membership and uses explicit bypass in the service.
[Authorize(Policy = AuthorizationPolicies.Authenticated)]
[Route("api/v1/staff-management")]
public sealed class StaffManagementController : ControllerBase
{
    private readonly IStaffManagementService _staff;

    public StaffManagementController(IStaffManagementService staff)
    {
        _staff = staff;
    }

    [AuthorizePermission(Permissions.Staff.Read)]
    [HttpGet("staff")]
    [ProducesResponseType(typeof(PagedResponse<StaffSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<StaffSummaryResponse>>> Search(
        [FromQuery] StaffSearchRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.SearchAsync(request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Staff.Read)]
    [HttpGet("staff/{staffMemberId:guid}")]
    [ProducesResponseType(typeof(StaffDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffDetailResponse>> GetById(
        Guid staffMemberId,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.GetByIdAsync(staffMemberId, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Staff.Manage)]
    [HttpPost("staff")]
    [ProducesResponseType(typeof(CreateStaffResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CreateStaffResponse>> Create(
        [FromBody] CreateStaffRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.CreateAsync(request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Staff.Manage)]
    [HttpPatch("staff/{staffMemberId:guid}")]
    [ProducesResponseType(typeof(StaffDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffDetailResponse>> Update(
        Guid staffMemberId,
        [FromBody] UpdateStaffRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.UpdateAsync(staffMemberId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Staff.Manage)]
    [HttpPost("staff/{staffMemberId:guid}/activate")]
    [ProducesResponseType(typeof(StaffDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffDetailResponse>> Activate(
        Guid staffMemberId,
        [FromBody] StaffActivationRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.ActivateAsync(staffMemberId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Staff.Manage)]
    [HttpPost("staff/{staffMemberId:guid}/deactivate")]
    [ProducesResponseType(typeof(StaffDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffDetailResponse>> Deactivate(
        Guid staffMemberId,
        [FromBody] StaffActivationRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.DeactivateAsync(staffMemberId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Roles.Read)]
    [HttpGet("roles")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffRoleInfoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffRoleInfoResponse>>> ListRoles(
        CancellationToken cancellationToken = default)
    {
        return Ok(await _staff.ListAssignableRolesAsync(cancellationToken));
    }

    [AuthorizePermission(Permissions.Roles.Assign)]
    [HttpPost("staff/{staffMemberId:guid}/roles/{roleName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AssignRole(
        Guid staffMemberId,
        string roleName,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        await _staff.AssignRoleAsync(staffMemberId, roleName, bypass, cancellationToken);
        return NoContent();
    }

    [AuthorizePermission(Permissions.Roles.Assign)]
    [HttpDelete("staff/{staffMemberId:guid}/roles/{roleName}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveRole(
        Guid staffMemberId,
        string roleName,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        await _staff.RemoveRoleAsync(staffMemberId, roleName, bypass, cancellationToken);
        return NoContent();
    }

    [AuthorizePermission(Permissions.Staff.Manage)]
    [HttpPost("staff/{staffMemberId:guid}/change-clinic")]
    [ProducesResponseType(typeof(StaffDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffDetailResponse>> ChangeClinic(
        Guid staffMemberId,
        [FromBody] ChangeStaffClinicRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.ChangeClinicAsync(staffMemberId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.Staff.PasswordReset)]
    [HttpPost("staff/{staffMemberId:guid}/password-reset")]
    [ProducesResponseType(typeof(StaffPasswordResetResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffPasswordResetResponse>> RequestPasswordReset(
        Guid staffMemberId,
        [FromBody] StaffPasswordResetRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.RequestPasswordResetAsync(staffMemberId, request, bypass, cancellationToken));
    }

    [AuthorizePermission(Permissions.SecuritySessions.Revoke)]
    [HttpPost("staff/{staffMemberId:guid}/revoke-sessions")]
    [ProducesResponseType(typeof(RevokeStaffSessionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RevokeStaffSessionsResponse>> RevokeSessions(
        Guid staffMemberId,
        [FromBody] RevokeStaffSessionsRequest request,
        [FromQuery] bool platformAdminBypass = false,
        CancellationToken cancellationToken = default)
    {
        var bypass = platformAdminBypass ? PlatformAdminBypass.Explicit : PlatformAdminBypass.None;
        return Ok(await _staff.RevokeSessionsAsync(staffMemberId, request, bypass, cancellationToken));
    }
}
