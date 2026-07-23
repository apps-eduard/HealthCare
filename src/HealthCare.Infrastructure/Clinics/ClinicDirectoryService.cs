using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Clinics;

public sealed class ClinicDirectoryService : IClinicDirectoryService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly ILogger<ClinicDirectoryService> _logger;

    public ClinicDirectoryService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        ILogger<ClinicDirectoryService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _logger = logger;
    }

    public async Task<PagedResponse<ClinicDirectoryItemResponse>> SearchAsync(
        ClinicSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var scope = ResolveDirectoryScope(request.OrganizationId, bypass);

        var query = ApplyScope(_dbContext.Clinics.AsNoTracking(), scope);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || c.Slug.ToLower().Contains(term)
                || (c.City != null && c.City.ToLower().Contains(term)));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(c => c.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var desc = request.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = request.SortBy.ToLowerInvariant();

        query = sortBy switch
        {
            "slug" => desc
                ? query.OrderByDescending(c => c.Slug).ThenBy(c => c.Id)
                : query.OrderBy(c => c.Slug).ThenBy(c => c.Id),
            "createdatutc" => desc
                ? query.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                : query.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id),
            "isactive" => desc
                ? query.OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ThenBy(c => c.Id)
                : query.OrderBy(c => c.IsActive).ThenBy(c => c.Name).ThenBy(c => c.Id),
            _ => desc
                ? query.OrderByDescending(c => c.Name).ThenBy(c => c.Id)
                : query.OrderBy(c => c.Name).ThenBy(c => c.Id),
        };

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? 20
            : Math.Min(request.PageSize, ClinicSearchRequestValidator.MaxPageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ClinicDirectoryItemResponse
            {
                ClinicId = c.Id,
                OrganizationId = c.OrganizationId,
                Name = c.Name,
                Slug = c.Slug,
                IsActive = c.IsActive,
                TimeZoneId = c.TimeZoneId,
                City = c.City,
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Clinic directory searched. ActorUserId={ActorUserId} OrganizationId={OrganizationId} ResultCount={ResultCount}",
            _currentUser.UserId,
            scope.OrganizationId,
            items.Count);

        return PagedResponse<ClinicDirectoryItemResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<ClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();

        var clinic = await _dbContext.Clinics.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken);

        if (clinic is null)
        {
            throw ClinicDirectoryException.NotFound();
        }

        if (!CanAccessClinic(clinic.OrganizationId, clinic.Id, bypass))
        {
            _audit.CrossTenantDenied(
                "clinic_directory_detail",
                ClinicErrorCodes.NotFound,
                clinic.OrganizationId,
                clinic.Id);
            throw ClinicDirectoryException.NotFound();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("clinic_directory_detail", clinic.OrganizationId, clinic.Id);
        }

        _logger.LogInformation(
            "Clinic directory detail accessed. ActorUserId={ActorUserId} ClinicId={ClinicId}",
            _currentUser.UserId,
            clinic.Id);

        return new ClinicDetailResponse
        {
            ClinicId = clinic.Id,
            OrganizationId = clinic.OrganizationId,
            Name = clinic.Name,
            Slug = clinic.Slug,
            IsActive = clinic.IsActive,
            TimeZoneId = clinic.TimeZoneId,
            Specialty = clinic.Specialty,
            Address = clinic.Address,
            City = clinic.City,
            PhoneNumber = clinic.PhoneNumber,
        };
    }

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw ClinicDirectoryException.DirectoryAccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Clinics.Read);
    }

    private DirectoryScope ResolveDirectoryScope(Guid? requestedOrganizationId, PlatformAdminBypass bypass)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (requestedOrganizationId is null || requestedOrganizationId == Guid.Empty)
            {
                throw ClinicDirectoryException.OrganizationScopeRequired();
            }

            _audit.ExplicitPlatformBypassUsed("clinic_directory_search", requestedOrganizationId, null);
            return new DirectoryScope(requestedOrganizationId.Value, ClinicId: null, SingleClinic: false);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        // Client OrganizationId never overrides trusted staff scope.
        if (requestedOrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "clinic_directory_org_override",
                ClinicErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw ClinicDirectoryException.InvalidScope();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            return new DirectoryScope(_currentStaff.OrganizationId, ClinicId: null, SingleClinic: false);
        }

        return new DirectoryScope(_currentStaff.OrganizationId, _currentStaff.ClinicId, SingleClinic: true);
    }

    private bool CanAccessClinic(Guid organizationId, Guid clinicId, PlatformAdminBypass bypass)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return true;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            return false;
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            return organizationId == _currentStaff.OrganizationId;
        }

        return clinicId == _currentStaff.ClinicId && organizationId == _currentStaff.OrganizationId;
    }

    private static IQueryable<Domain.Clinics.Clinic> ApplyScope(
        IQueryable<Domain.Clinics.Clinic> query,
        DirectoryScope scope)
    {
        query = query.Where(c => c.OrganizationId == scope.OrganizationId);
        if (scope.SingleClinic && scope.ClinicId is Guid clinicId)
        {
            query = query.Where(c => c.Id == clinicId);
        }

        return query;
    }

    private sealed record DirectoryScope(Guid OrganizationId, Guid? ClinicId, bool SingleClinic);
}
