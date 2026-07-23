using HealthCare.Application.Authorization;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;

namespace HealthCare.Application.Clinics;

public interface IClinicDirectoryService
{
    Task<PagedResponse<ClinicDirectoryItemResponse>> SearchAsync(
        ClinicSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);

    Task<ClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}
