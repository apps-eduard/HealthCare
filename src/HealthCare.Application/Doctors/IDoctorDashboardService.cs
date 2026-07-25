using HealthCare.Application.Authorization;
using HealthCare.Contracts.Doctors;

namespace HealthCare.Application.Doctors;

public interface IDoctorDashboardService
{
    Task<DoctorDashboardResponse> GetAsync(
        DoctorDashboardQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}
