using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Clinics;

/// <summary>
/// Clinic-filtered operational audit reads for CLINIC_ADMIN (and PLATFORM_ADMIN with explicit bypass).
/// Uses OrganizationAuditEvents with an explicit action allowlist. Successful reads are not audited
/// (same policy as organization audit-log reads). No raw metadata is stored or returned.
/// </summary>
public sealed class ClinicAuditLogService : IClinicAuditLogService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly AuditRetentionOptions _retention;

    public ClinicAuditLogService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IClinicTimeZoneConverter timeZones,
        IOptions<AuditRetentionOptions> retention)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _timeZones = timeZones;
        _retention = retention.Value;
    }

    public async Task<ClinicAuditLogListResponse> SearchAsync(
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        return await QueryAsync(scope, query, correlationOverride: null, cancellationToken);
    }

    public async Task<ClinicAuditLogDetailResponse> GetByIdAsync(
        Guid eventId,
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        var row = await _dbContext.OrganizationAuditEvents.AsNoTracking()
            .Where(e => e.Id == eventId
                && e.ClinicId == scope.ClinicId
                && ClinicAuditActionAllowlist.ActionValues.Contains(e.Action))
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ClinicAuditLogException.NotFound();

        var item = await MapItemsAsync([row], scope, cancellationToken);
        return new ClinicAuditLogDetailResponse
        {
            RetentionDays = Math.Max(1, _retention.RetentionDays),
            TimeZoneId = scope.TimeZoneId,
            Event = item[0],
        };
    }

    public async Task<ClinicAuditLogListResponse> GetByCorrelationIdAsync(
        string correlationId,
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw ClinicAuditLogException.InvalidScope();
        }

        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        return await QueryAsync(scope, query, correlationOverride: correlationId.Trim(), cancellationToken);
    }

    private async Task<ClinicAuditLogListResponse> QueryAsync(
        ResolvedClinicAuditScope scope,
        ClinicAuditLogQuery query,
        string? correlationOverride,
        CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1
            ? ClinicAuditLogQueryValidator.DefaultPageSize
            : Math.Min(query.PageSize, ClinicAuditLogQueryValidator.MaxPageSize);

        var events = _dbContext.OrganizationAuditEvents.AsNoTracking()
            .Where(e => e.ClinicId == scope.ClinicId
                && ClinicAuditActionAllowlist.ActionValues.Contains(e.Action));

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
            if (!ClinicAuditActionAllowlist.IsAllowed(action))
            {
                return EmptyList(scope, page, pageSize);
            }

            events = events.Where(e => e.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.ResultCode))
        {
            var result = query.ResultCode.Trim();
            events = events.Where(e => e.ResultCode == result);
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            var resourceType = query.ResourceType.Trim();
            events = events.Where(e => e.ResourceType == resourceType);
        }

        var correlation = correlationOverride ?? query.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlation))
        {
            var cid = correlation.Trim();
            events = events.Where(e => e.CorrelationId == cid);
        }

        if (!string.IsNullOrWhiteSpace(query.FromDate) || !string.IsNullOrWhiteSpace(query.ToDate))
        {
            if (string.IsNullOrWhiteSpace(query.FromDate)
                || string.IsNullOrWhiteSpace(query.ToDate)
                || !DateOnly.TryParse(query.FromDate, out var from)
                || !DateOnly.TryParse(query.ToDate, out var to)
                || from > to
                || to.DayNumber - from.DayNumber + 1 > ClinicAuditLogQueryValidator.MaxInclusiveDays)
            {
                throw ClinicAuditLogException.InvalidDateRange();
            }

            var rangeStart = _timeZones.ToUtc(from, TimeOnly.MinValue, scope.TimeZoneId);
            var rangeEndExclusive = _timeZones.ToUtc(to.AddDays(1), TimeOnly.MinValue, scope.TimeZoneId);
            events = events.Where(e => e.OccurredAtUtc >= rangeStart && e.OccurredAtUtc < rangeEndExclusive);
        }

        var totalCount = await events.CountAsync(cancellationToken);
        var pageRows = await events
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ClinicAuditLogListResponse
        {
            ClinicId = scope.ClinicId,
            ClinicName = scope.ClinicName,
            OrganizationId = scope.OrganizationId,
            TimeZoneId = scope.TimeZoneId,
            RetentionDays = Math.Max(1, _retention.RetentionDays),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = await MapItemsAsync(pageRows, scope, cancellationToken),
        };
    }

    private async Task<IReadOnlyList<ClinicAuditLogItem>> MapItemsAsync(
        IReadOnlyList<OrganizationAuditEvent> rows,
        ResolvedClinicAuditScope scope,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var actorIds = rows.Where(r => r.ActorUserId.HasValue).Select(r => r.ActorUserId!.Value).Distinct().ToList();
        var emails = await _dbContext.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(x => x.Id, x => x.Email, cancellationToken);

        var roles = await _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.ClinicId == scope.ClinicId && actorIds.Contains(s.UserId))
            .Select(s => new { s.UserId, s.Role })
            .ToListAsync(cancellationToken);
        var roleMap = roles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.First().Role);

        return rows.Select(row =>
        {
            string? actorDisplay = null;
            string? actorRole = null;
            if (row.ActorUserId is Guid actorId)
            {
                if (emails.TryGetValue(actorId, out var email) && !string.IsNullOrWhiteSpace(email))
                {
                    actorDisplay = TruncateActor(email);
                }

                if (roleMap.TryGetValue(actorId, out var role))
                {
                    actorRole = role;
                }
            }

            return new ClinicAuditLogItem
            {
                AuditLogId = row.Id,
                OccurredAtUtc = row.OccurredAtUtc,
                Category = row.Category,
                Action = row.Action,
                ResultCode = row.ResultCode,
                ActorUserId = row.ActorUserId,
                ActorDisplayName = actorDisplay,
                ActorRole = actorRole,
                ResourceType = row.ResourceType,
                ResourceId = row.ResourceId,
                CorrelationId = row.CorrelationId,
                ClinicId = scope.ClinicId,
                ClinicName = scope.ClinicName,
                Summary = ClinicAuditActionAllowlist.ToSummary(row.Action),
            };
        }).ToList();
    }

    private async Task<ResolvedClinicAuditScope> ResolveScopeAsync(
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        EnsureAuthorized();
        var clinicId = await ResolveClinicIdAsync(query, bypass, cancellationToken);

        var clinic = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => new { c.Id, c.Name, c.OrganizationId, c.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ClinicAuditLogException.ClinicNotFound();

        return new ResolvedClinicAuditScope(
            clinic.Id,
            clinic.Name,
            clinic.OrganizationId,
            clinic.TimeZoneId);
    }

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw ClinicAuditLogException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Clinics.AuditLogsRead);
    }

    private async Task<Guid> ResolveClinicIdAsync(
        ClinicAuditLogQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.ClinicId is null || query.ClinicId == Guid.Empty)
            {
                throw ClinicAuditLogException.ClinicScopeRequired();
            }

            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(c => c.Id == query.ClinicId.Value, cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "clinic_audit_logs",
                    ClinicAuditLogErrorCodes.ClinicNotFound,
                    null,
                    query.ClinicId);
                throw ClinicAuditLogException.ClinicNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("clinic_audit_logs", null, query.ClinicId);
            return query.ClinicId.Value;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (query.ClinicId is Guid requested
            && requested != Guid.Empty
            && requested != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied(
                "clinic_audit_logs_clinic_override",
                ClinicAuditLogErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                requested);
            throw ClinicAuditLogException.InvalidScope();
        }

        if (_currentStaff.ClinicId == Guid.Empty)
        {
            throw ClinicAuditLogException.ClinicNotFound();
        }

        return _currentStaff.ClinicId;
    }

    private ClinicAuditLogListResponse EmptyList(
        ResolvedClinicAuditScope scope,
        int page,
        int pageSize) =>
        new()
        {
            ClinicId = scope.ClinicId,
            ClinicName = scope.ClinicName,
            OrganizationId = scope.OrganizationId,
            TimeZoneId = scope.TimeZoneId,
            RetentionDays = Math.Max(1, _retention.RetentionDays),
            TotalCount = 0,
            Page = page,
            PageSize = pageSize,
            Items = [],
        };

    private static string TruncateActor(string email)
    {
        var local = email.Split('@')[0].Trim();
        if (string.IsNullOrWhiteSpace(local))
        {
            return "actor";
        }

        return local.Length <= 18 ? local : local[..14] + "…";
    }

    private sealed record ResolvedClinicAuditScope(
        Guid ClinicId,
        string ClinicName,
        Guid OrganizationId,
        string TimeZoneId);
}
