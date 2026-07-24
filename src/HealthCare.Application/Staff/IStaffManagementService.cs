using HealthCare.Application.Authorization;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Staff;

namespace HealthCare.Application.Staff;

public interface IStaffManagementService
{
    Task<PagedResponse<StaffSummaryResponse>> SearchAsync(
        StaffSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> GetByIdAsync(
        Guid staffMemberId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<CreateStaffResponse> CreateAsync(
        CreateStaffRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> UpdateAsync(
        Guid staffMemberId,
        UpdateStaffRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> ActivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> DeactivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffRoleInfoResponse>> ListAssignableRolesAsync(
        CancellationToken cancellationToken = default);

    Task AssignRoleAsync(
        Guid staffMemberId,
        string roleName,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task RemoveRoleAsync(
        Guid staffMemberId,
        string roleName,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Organization-scoped Clinic Admin directory (same rules as staff search with Role=CLINIC_ADMIN).
    /// </summary>
    Task<PagedResponse<StaffSummaryResponse>> SearchClinicAdminsAsync(
        StaffSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffDetailResponse> ChangeClinicAsync(
        Guid staffMemberId,
        ChangeStaffClinicRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<StaffPasswordResetResponse> RequestPasswordResetAsync(
        Guid staffMemberId,
        StaffPasswordResetRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<RevokeStaffSessionsResponse> RevokeSessionsAsync(
        Guid staffMemberId,
        RevokeStaffSessionsRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}
