using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Organizations;

public sealed class OrganizationDirectoryService : IOrganizationDirectoryService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly ILogger<OrganizationDirectoryService> _logger;

    public OrganizationDirectoryService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        ILogger<OrganizationDirectoryService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _permissions = permissions;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PagedResponse<OrganizationDirectoryItemResponse>> SearchAsync(
        OrganizationSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();

        var query = _dbContext.Organizations.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(o =>
                o.Name.ToLower().Contains(term)
                || o.Slug.ToLower().Contains(term));
        }

        if (request.IsActive.HasValue)
        {
            var active = request.IsActive.Value
                ? OrganizationStatus.Active
                : OrganizationStatus.Inactive;
            query = query.Where(o => o.Status == active);
        }

        var projected = query.Select(o => new
        {
            o.Id,
            o.Name,
            o.Slug,
            o.Status,
            o.CreatedAtUtc,
            ClinicCount = o.Clinics.Count,
        });

        var totalCount = await projected.CountAsync(cancellationToken);
        var desc = request.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = request.SortBy.ToLowerInvariant();

        projected = sortBy switch
        {
            "slug" => desc
                ? projected.OrderByDescending(o => o.Slug).ThenBy(o => o.Id)
                : projected.OrderBy(o => o.Slug).ThenBy(o => o.Id),
            "createdatutc" => desc
                ? projected.OrderByDescending(o => o.CreatedAtUtc).ThenBy(o => o.Id)
                : projected.OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id),
            "isactive" => desc
                ? projected.OrderByDescending(o => o.Status).ThenBy(o => o.Name).ThenBy(o => o.Id)
                : projected.OrderBy(o => o.Status).ThenBy(o => o.Name).ThenBy(o => o.Id),
            "cliniccount" => desc
                ? projected.OrderByDescending(o => o.ClinicCount).ThenBy(o => o.Name).ThenBy(o => o.Id)
                : projected.OrderBy(o => o.ClinicCount).ThenBy(o => o.Name).ThenBy(o => o.Id),
            _ => desc
                ? projected.OrderByDescending(o => o.Name).ThenBy(o => o.Id)
                : projected.OrderBy(o => o.Name).ThenBy(o => o.Id),
        };

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? 20
            : Math.Min(request.PageSize, OrganizationSearchRequestValidator.MaxPageSize);

        var items = await projected
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrganizationDirectoryItemResponse
            {
                OrganizationId = o.Id,
                Name = o.Name,
                Slug = o.Slug,
                IsActive = o.Status == OrganizationStatus.Active,
                ClinicCount = o.ClinicCount,
                CreatedAtUtc = o.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Organization directory searched. ActorUserId={ActorUserId} ResultCount={ResultCount} CorrelationId={CorrelationId}",
            _currentUser.UserId,
            items.Count,
            string.Empty);

        _audit.ExplicitPlatformBypassUsed("organization_directory_search", organizationId: null, clinicId: null);

        return PagedResponse<OrganizationDirectoryItemResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<OrganizationDetailResponse> GetByIdAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();

        if (organizationId == Guid.Empty)
        {
            throw OrganizationDirectoryException.NotFound();
        }

        var org = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.Slug,
                o.Status,
                o.CreatedAtUtc,
                ClinicCount = o.Clinics.Count,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (org is null)
        {
            throw OrganizationDirectoryException.NotFound();
        }

        _logger.LogInformation(
            "Organization directory detail accessed. ActorUserId={ActorUserId} OrganizationId={OrganizationId}",
            _currentUser.UserId,
            org.Id);

        _audit.ExplicitPlatformBypassUsed("organization_directory_detail", org.Id, clinicId: null);

        return new OrganizationDetailResponse
        {
            OrganizationId = org.Id,
            Name = org.Name,
            Slug = org.Slug,
            IsActive = org.Status == OrganizationStatus.Active,
            ClinicCount = org.ClinicCount,
            CreatedAtUtc = org.CreatedAtUtc,
        };
    }

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (!_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.PermissionDenied(
                Permissions.Organizations.Read,
                "organization_directory",
                OrganizationErrorCodes.DirectoryAccessDenied);
            throw OrganizationDirectoryException.DirectoryAccessDenied();
        }

        _permissions.RequirePermission(Permissions.Organizations.Read);
    }
}
