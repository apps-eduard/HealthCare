using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Organizations;

public sealed class OrganizationAuditLogService : IOrganizationAuditLogService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly AuditRetentionOptions _retention;

    public OrganizationAuditLogService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IOptions<AuditRetentionOptions> retention)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _retention = retention.Value;
    }

    public async Task<OrganizationAuditLogListResponse> SearchAsync(
        OrganizationAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        return await QueryAsync(scope, query, correlationOverride: null, cancellationToken);
    }

    public async Task<OrganizationAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        OrganizationAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        var row = await _dbContext.OrganizationAuditEvents.AsNoTracking()
            .Where(e => e.Id == eventId && e.OrganizationId == scope.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw OrganizationAuditLogException.NotFound();

        if (scope.ClinicId.HasValue && row.ClinicId != scope.ClinicId.Value)
        {
            throw OrganizationAuditLogException.NotFound();
        }

        var clinicName = await ResolveClinicNameAsync(row.ClinicId, cancellationToken);
        return new OrganizationAuditLogDetailResponse
        {
            RetentionDays = Math.Max(1, _retention.RetentionDays),
            Event = MapItem(row, clinicName),
        };
    }

    public async Task<OrganizationAuditLogListResponse> GetByCorrelationIdAsync(
        string correlationId,
        OrganizationAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw OrganizationAuditLogException.InvalidScope();
        }

        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        return await QueryAsync(scope, query, correlationOverride: correlationId.Trim(), cancellationToken);
    }

    private async Task<OrganizationAuditLogListResponse> QueryAsync(
        AuditScope scope,
        OrganizationAuditLogQuery query,
        string? correlationOverride,
        CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1
            ? OrganizationAuditLogQueryValidator.DefaultPageSize
            : Math.Min(query.PageSize, OrganizationAuditLogQueryValidator.MaxPageSize);

        var events = _dbContext.OrganizationAuditEvents.AsNoTracking()
            .Where(e => e.OrganizationId == scope.OrganizationId);

        if (scope.ClinicId.HasValue)
        {
            events = events.Where(e => e.ClinicId == scope.ClinicId.Value);
        }

        if (query.ActorUserId is Guid actor && actor != Guid.Empty)
        {
            events = events.Where(e => e.ActorUserId == actor);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            events = events.Where(e => e.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            events = events.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.ResultCode))
        {
            var result = query.ResultCode.Trim();
            events = events.Where(e => e.ResultCode == result);
        }

        var correlation = correlationOverride ?? query.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlation))
        {
            var cid = correlation.Trim();
            events = events.Where(e => e.CorrelationId == cid);
        }

        if (!string.IsNullOrWhiteSpace(query.FromUtc) || !string.IsNullOrWhiteSpace(query.ToUtc))
        {
            if (string.IsNullOrWhiteSpace(query.FromUtc)
                || string.IsNullOrWhiteSpace(query.ToUtc)
                || !DateTimeOffset.TryParse(query.FromUtc, out var from)
                || !DateTimeOffset.TryParse(query.ToUtc, out var to)
                || from > to
                || (to - from).TotalDays > OrganizationAuditLogQueryValidator.MaxInclusiveDays)
            {
                throw OrganizationAuditLogException.InvalidDateRange();
            }

            events = events.Where(e => e.OccurredAtUtc >= from && e.OccurredAtUtc <= to);
        }

        var totalCount = await events.CountAsync(cancellationToken);
        var pageRows = await events
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var clinicIds = pageRows.Where(r => r.ClinicId.HasValue).Select(r => r.ClinicId!.Value).Distinct().ToList();
        var clinicNames = await _dbContext.Clinics.AsNoTracking()
            .Where(c => clinicIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return new OrganizationAuditLogListResponse
        {
            OrganizationId = scope.OrganizationId,
            OrganizationName = scope.OrganizationName,
            ClinicId = scope.ClinicId,
            RetentionDays = Math.Max(1, _retention.RetentionDays),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = pageRows.Select(r => MapItem(
                r,
                r.ClinicId is Guid cid && clinicNames.TryGetValue(cid, out var name) ? name : null)).ToList(),
        };
    }

    private async Task<AuditScope> ResolveScopeAsync(
        OrganizationAuditLogQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        EnsureAuthorized();

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.OrganizationId is null || query.OrganizationId == Guid.Empty)
            {
                throw OrganizationAuditLogException.OrganizationScopeRequired();
            }

            var org = await _dbContext.Organizations.AsNoTracking()
                .Where(o => o.Id == query.OrganizationId.Value)
                .Select(o => new { o.Id, o.Name })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw OrganizationAuditLogException.OrganizationNotFound();

            Guid? clinicId = null;
            if (query.ClinicId is Guid requestedClinic && requestedClinic != Guid.Empty)
            {
                var clinicOk = await _dbContext.Clinics.AsNoTracking()
                    .AnyAsync(c => c.Id == requestedClinic && c.OrganizationId == org.Id, cancellationToken);
                if (!clinicOk)
                {
                    _audit.CrossTenantDenied(
                        "organization_audit_clinic",
                        OrganizationAuditLogErrorCodes.ClinicNotFound,
                        org.Id,
                        requestedClinic);
                    throw OrganizationAuditLogException.ClinicNotFound();
                }

                clinicId = requestedClinic;
            }

            _audit.ExplicitPlatformBypassUsed("organization_audit_logs", org.Id, clinicId);
            return new AuditScope(org.Id, org.Name, clinicId);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw OrganizationAuditLogException.AccessDenied();
        }

        if (query.OrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "organization_audit_org_override",
                OrganizationAuditLogErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw OrganizationAuditLogException.InvalidScope();
        }

        var organizationName = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == _currentStaff.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw OrganizationAuditLogException.OrganizationNotFound();

        Guid? scopedClinicId = null;
        if (query.ClinicId is Guid clinicFilter && clinicFilter != Guid.Empty)
        {
            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(
                    c => c.Id == clinicFilter && c.OrganizationId == _currentStaff.OrganizationId,
                    cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "organization_audit_clinic",
                    OrganizationAuditLogErrorCodes.ClinicNotFound,
                    _currentStaff.OrganizationId,
                    clinicFilter);
                throw OrganizationAuditLogException.ClinicNotFound();
            }

            scopedClinicId = clinicFilter;
        }

        return new AuditScope(_currentStaff.OrganizationId, organizationName, scopedClinicId);
    }

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw OrganizationAuditLogException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Organizations.AuditLogsRead);
    }

    private async Task<string?> ResolveClinicNameAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        if (clinicId is null)
        {
            return null;
        }

        return await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId.Value)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static OrganizationAuditLogItem MapItem(Domain.Organizations.OrganizationAuditEvent row, string? clinicName) =>
        new()
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            ClinicId = row.ClinicId,
            ClinicName = clinicName,
            ActorUserId = row.ActorUserId,
            Category = row.Category,
            Action = row.Action,
            ResultCode = row.ResultCode,
            ResourceType = row.ResourceType,
            ResourceId = row.ResourceId,
            CorrelationId = row.CorrelationId,
            OccurredAtUtc = row.OccurredAtUtc,
        };

    private sealed record AuditScope(Guid OrganizationId, string OrganizationName, Guid? ClinicId);
}
