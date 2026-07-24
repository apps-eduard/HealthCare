using HealthCare.Application.Authorization;
using HealthCare.Contracts.Clinics;

namespace HealthCare.Application.Clinics;

public interface IClinicDashboardService
{
    Task<ClinicDashboardResponse> GetAsync(
        ClinicDashboardQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}
