using HealthCare.Application.Authorization;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;

namespace HealthCare.Application.Clinics;

public interface IClinicManagementService
{
    Task<PagedResponse<OrganizationClinicListItemResponse>> SearchAsync(
        OrganizationClinicSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> CreateAsync(
        CreateOrganizationClinicRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> UpdateAsync(
        Guid clinicId,
        UpdateOrganizationClinicRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> ActivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<OrganizationClinicDetailResponse> DeactivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}
