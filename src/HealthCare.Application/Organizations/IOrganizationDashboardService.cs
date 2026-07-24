using HealthCare.Application.Authorization;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public interface IOrganizationDashboardService
{
    Task<OrganizationDashboardResponse> GetAsync(
        OrganizationDashboardQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default);
}
