using HealthCare.Contracts.Common;
using HealthCare.Contracts.Organizations;

namespace HealthCare.Application.Organizations;

public interface IOrganizationDirectoryService
{
    Task<PagedResponse<OrganizationDirectoryItemResponse>> SearchAsync(
        OrganizationSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<OrganizationDetailResponse> GetByIdAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
