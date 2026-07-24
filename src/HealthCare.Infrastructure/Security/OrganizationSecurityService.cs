using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Security;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Security;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Security;

public sealed class OrganizationSecurityService : IOrganizationSecurityService
{
    public const int MaxRecentEventItems = 50;

    private readonly HealthCareDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly ISecuritySessionInvalidationService _sessions;
    private readonly ISecurityEventRecorder _events;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrganizationSecurityService> _logger;

    public OrganizationSecurityService(
        HealthCareDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        ISecuritySessionInvalidationService sessions,
        ISecurityEventRecorder events,
        TimeProvider timeProvider,
        ILogger<OrganizationSecurityService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _sessions = sessions;
        _events = events;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OrganizationSecuritySessionListResponse> ListSessionsAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.SecuritySessions.Read);
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1
            ? OrganizationSecurityQueryValidator.DefaultPageSize
            : Math.Min(query.PageSize, OrganizationSecurityQueryValidator.MaxPageSize);

        var staffQuery = _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.OrganizationId == scope.OrganizationId);
        if (scope.ClinicId.HasValue)
        {
            staffQuery = staffQuery.Where(s => s.ClinicId == scope.ClinicId.Value);
        }

        if (query.StaffMemberId.HasValue)
        {
            staffQuery = staffQuery.Where(s => s.Id == query.StaffMemberId.Value);
        }

        if (query.UserId.HasValue)
        {
            staffQuery = staffQuery.Where(s => s.UserId == query.UserId.Value);
        }

        var staffRows = await staffQuery
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.ClinicId,
                s.Role,
                Display = !string.IsNullOrWhiteSpace(s.DisplayName)
                    ? s.DisplayName!
                    : (s.FirstName + " " + s.LastName).Trim(),
            })
            .ToListAsync(cancellationToken);

        var userIds = staffRows.Select(s => s.UserId).Distinct().ToList();
        var clinicIds = staffRows.Select(s => s.ClinicId).Distinct().ToList();
        var clinicNames = await _dbContext.Clinics.AsNoTracking()
            .Where(c => clinicIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var staffByUser = staffRows
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.First());

        IQueryable<RefreshToken> tokens = _dbContext.RefreshTokens.AsNoTracking()
            .Where(t => userIds.Contains(t.UserId));
        if (!query.IncludeRevoked)
        {
            tokens = tokens.Where(t => t.RevokedAtUtc == null && t.ExpiresAtUtc > now);
        }

        var totalCount = await tokens.CountAsync(cancellationToken);
        var activeCount = await _dbContext.RefreshTokens.AsNoTracking()
            .Where(t => userIds.Contains(t.UserId) && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .CountAsync(cancellationToken);

        var pageRows = await tokens
            .OrderByDescending(t => t.CreatedAtUtc)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.UserId,
                t.CreatedAtUtc,
                t.ExpiresAtUtc,
                t.RevokedAtUtc,
                t.RevokedReason,
                t.CreatedByIp,
                t.CreatedByUserAgent,
            })
            .ToListAsync(cancellationToken);

        var items = pageRows.Select(t =>
        {
            staffByUser.TryGetValue(t.UserId, out var staff);
            return new OrganizationSecuritySessionItem
            {
                SessionId = t.Id,
                UserId = t.UserId,
                StaffMemberId = staff?.Id,
                StaffDisplayName = string.IsNullOrWhiteSpace(staff?.Display) ? null : staff.Display,
                StaffRole = staff?.Role,
                ClinicId = staff?.ClinicId,
                ClinicName = staff is null ? null : clinicNames.GetValueOrDefault(staff.ClinicId),
                CreatedAtUtc = t.CreatedAtUtc,
                ExpiresAtUtc = t.ExpiresAtUtc,
                RevokedAtUtc = t.RevokedAtUtc,
                RevokedReason = t.RevokedReason,
                IsActive = t.RevokedAtUtc is null && t.ExpiresAtUtc > now,
                CreatedByIp = t.CreatedByIp,
                CreatedByUserAgent = TruncateAgent(t.CreatedByUserAgent),
            };
        }).ToList();

        _audit.SecurityOperation(
            "security_sessions_list",
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId,
            targetUserId: query.UserId);

        return new OrganizationSecuritySessionListResponse
        {
            OrganizationId = scope.OrganizationId,
            OrganizationName = scope.OrganizationName,
            ClinicId = scope.ClinicId,
            ActiveSessionCount = activeCount,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = items,
        };
    }

    public async Task<RevokeOrganizationSessionsResponse> RevokeStaffSessionsAsync(
        Guid staffMemberId,
        RevokeOrganizationSessionsRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.SecuritySessions.Revoke);
        var scope = await ResolveScopeAsync(new OrganizationSecurityQuery(), bypass, cancellationToken);
        var staff = await LoadOrgStaffAsync(staffMemberId, scope, cancellationToken);
        EnsureNotPlatformAdmin(staff);

        var count = await _sessions.InvalidateUserSessionsAsync(
            staff.UserId,
            "OrganizationSecuritySessionsRevoked",
            cancellationToken);

        _events.TryRecord(new SecurityEventWrite
        {
            EventType = SecurityEventType.SessionRevoked,
            Operation = "organization_security_session_revoke",
            ReasonCode = "session_revoked",
            OrganizationId = staff.OrganizationId,
            ClinicId = staff.ClinicId,
            ActorUserId = _currentUser.UserId,
            TargetUserId = staff.UserId,
            TargetStaffMemberId = staff.Id,
        });

        _logger.LogInformation(
            "Organization security sessions revoked. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} Count={Count} Reason={Reason}",
            _currentUser.UserId,
            staff.Id,
            count,
            request.Reason);
        _audit.SecurityOperation(
            "security_sessions_revoke",
            "succeeded",
            staff.OrganizationId,
            staff.ClinicId,
            staff.UserId);

        return new RevokeOrganizationSessionsResponse
        {
            Message = "Active sessions were revoked.",
            RevokedRefreshTokenCount = count,
        };
    }

    public async Task<CompromisedAccountResponseResult> RespondToCompromisedAccountAsync(
        Guid staffMemberId,
        CompromisedAccountResponseRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.SecuritySessions.Revoke);
        var scope = await ResolveScopeAsync(new OrganizationSecurityQuery(), bypass, cancellationToken);
        var staff = await _dbContext.StaffMembers
            .SingleOrDefaultAsync(
                s => s.Id == staffMemberId && s.OrganizationId == scope.OrganizationId,
                cancellationToken)
            ?? throw OrganizationSecurityException.TargetNotFound();

        if (scope.ClinicId.HasValue && staff.ClinicId != scope.ClinicId.Value)
        {
            throw OrganizationSecurityException.TargetNotFound();
        }

        EnsureNotPlatformAdmin(staff);

        if (_currentUser.UserId == staff.UserId)
        {
            throw OrganizationSecurityException.SelfCompromiseDenied();
        }

        if (!staff.IsActive)
        {
            throw OrganizationSecurityException.AlreadyInactive();
        }

        if (staff.Version != request.ExpectedVersion)
        {
            throw new OrganizationSecurityException(
                OrganizationSecurityErrorCodes.AlreadyInactive,
                "The staff membership was modified by another request.",
                409);
        }

        await EnsureNotLastAdminAsync(staff, cancellationToken);

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw OrganizationSecurityException.TargetNotFound();

        staff.IsActive = false;
        staff.Version++;
        staff.UpdatedAtUtc = _timeProvider.GetUtcNow();
        user.IsActive = false;
        user.UpdatedAtUtc = staff.UpdatedAtUtc;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var count = await _sessions.InvalidateUserSessionsAsync(
            staff.UserId,
            "CompromisedAccountResponse",
            cancellationToken);

        _events.TryRecord(new SecurityEventWrite
        {
            EventType = SecurityEventType.CompromisedAccountResponse,
            Operation = "organization_security_compromise_response",
            ReasonCode = "compromised_account",
            OrganizationId = staff.OrganizationId,
            ClinicId = staff.ClinicId,
            ActorUserId = _currentUser.UserId,
            TargetUserId = staff.UserId,
            TargetStaffMemberId = staff.Id,
        });

        _logger.LogInformation(
            "Compromised account response applied. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} Revoked={Count}",
            _currentUser.UserId,
            staff.Id,
            count);
        _audit.SecurityOperation(
            "security_compromise_response",
            "succeeded",
            staff.OrganizationId,
            staff.ClinicId,
            staff.UserId);

        return new CompromisedAccountResponseResult
        {
            Message = "Account deactivated and sessions revoked.",
            StaffMemberId = staff.Id,
            UserId = staff.UserId,
            RevokedRefreshTokenCount = count,
        };
    }

    public async Task<OrganizationFailedLoginSummaryResponse> GetFailedLoginSummaryAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.SecuritySessions.Read);
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        var (fromUtc, toUtc) = ResolveEventRange(query);
        var now = _timeProvider.GetUtcNow();

        var staffQuery = _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.OrganizationId == scope.OrganizationId);
        if (scope.ClinicId.HasValue)
        {
            staffQuery = staffQuery.Where(s => s.ClinicId == scope.ClinicId.Value);
        }

        var staff = await staffQuery
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.ClinicId,
                Display = !string.IsNullOrWhiteSpace(s.DisplayName)
                    ? s.DisplayName!
                    : (s.FirstName + " " + s.LastName).Trim(),
            })
            .ToListAsync(cancellationToken);

        var userIds = staff.Select(s => s.UserId).Distinct().ToList();
        var users = userIds.Count == 0
            ? []
            : await _dbContext.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id)
                    && (u.AccessFailedCount > 0 || (u.LockoutEnd != null && u.LockoutEnd > now)))
                .Select(u => new { u.Id, u.AccessFailedCount, u.LockoutEnd })
                .ToListAsync(cancellationToken);

        var clinicIds = staff.Select(s => s.ClinicId).Distinct().ToList();
        var clinicNames = await _dbContext.Clinics.AsNoTracking()
            .Where(c => clinicIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var eventCounts = await _dbContext.SecurityEvents.AsNoTracking()
            .Where(e => e.OrganizationId == scope.OrganizationId
                && e.EventType == SecurityEventType.FailedLogin
                && e.OccurredAtUtc >= fromUtc
                && e.OccurredAtUtc <= toUtc
                && e.TargetUserId != null
                && userIds.Contains(e.TargetUserId.Value))
            .GroupBy(e => e.TargetUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        var totalEvents = eventCounts.Values.Sum();
        var staffByUser = staff.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.First());

        var items = users
            .Select(u =>
            {
                staffByUser.TryGetValue(u.Id, out var s);
                var locked = u.LockoutEnd.HasValue && u.LockoutEnd > now;
                return new OrganizationFailedLoginUserItem
                {
                    UserId = u.Id,
                    StaffMemberId = s?.Id,
                    StaffDisplayName = string.IsNullOrWhiteSpace(s?.Display) ? null : s.Display,
                    ClinicId = s?.ClinicId,
                    ClinicName = s is null ? null : clinicNames.GetValueOrDefault(s.ClinicId),
                    AccessFailedCount = u.AccessFailedCount,
                    LockoutEndUtc = u.LockoutEnd,
                    IsLockedOut = locked,
                    RecentFailedLoginEvents = eventCounts.GetValueOrDefault(u.Id),
                };
            })
            .OrderByDescending(x => x.AccessFailedCount)
            .ThenByDescending(x => x.RecentFailedLoginEvents)
            .Take(100)
            .ToList();

        _audit.SecurityOperation(
            "security_failed_login_summary",
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId);

        return new OrganizationFailedLoginSummaryResponse
        {
            OrganizationId = scope.OrganizationId,
            OrganizationName = scope.OrganizationName,
            UsersWithFailedAttempts = items.Count,
            CurrentlyLockedOutUsers = items.Count(x => x.IsLockedOut),
            FailedLoginEventsInRange = totalEvents,
            Users = items,
        };
    }

    public Task<OrganizationSecurityEventSummaryResponse> GetAuthorizationDenialSummaryAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default) =>
        GetEventSummaryAsync(
            query,
            bypass,
            "authorization_denials",
            e => e.EventType == SecurityEventType.PermissionDenied,
            cancellationToken);

    public Task<OrganizationSecurityEventSummaryResponse> GetCrossClinicAttemptSummaryAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default) =>
        GetEventSummaryAsync(
            query,
            bypass,
            "cross_clinic_attempts",
            e => e.EventType == SecurityEventType.CrossTenantDenied
                 && (e.ReasonCode == AuthorizationErrorCodes.ClinicAccessDenied
                     || e.Operation.Contains("cross_clinic")),
            cancellationToken);

    private async Task<OrganizationSecurityEventSummaryResponse> GetEventSummaryAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass,
        string category,
        System.Linq.Expressions.Expression<Func<SecurityEvent, bool>> predicate,
        CancellationToken cancellationToken)
    {
        EnsureAuthorized(Permissions.SecuritySessions.Read);
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        var (fromUtc, toUtc) = ResolveEventRange(query);

        var events = _dbContext.SecurityEvents.AsNoTracking()
            .Where(e => e.OrganizationId == scope.OrganizationId
                && e.OccurredAtUtc >= fromUtc
                && e.OccurredAtUtc <= toUtc)
            .Where(predicate);

        if (scope.ClinicId.HasValue)
        {
            var clinicId = scope.ClinicId.Value;
            events = events.Where(e => e.ClinicId == null || e.ClinicId == clinicId);
        }

        var rows = await events
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        var clinicIds = rows.Where(r => r.ClinicId.HasValue).Select(r => r.ClinicId!.Value).Distinct().ToList();
        var clinicNames = clinicIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Clinics.AsNoTracking()
                .Where(c => clinicIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var byOperation = rows
            .GroupBy(r => new { r.Operation, r.ReasonCode })
            .Select(g => new OrganizationSecurityEventCountByOperation
            {
                Operation = g.Key.Operation,
                ReasonCode = g.Key.ReasonCode,
                Count = g.Count(),
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        var byClinic = rows
            .GroupBy(r => r.ClinicId)
            .Select(g => new OrganizationSecurityEventCountByClinic
            {
                ClinicId = g.Key,
                ClinicName = g.Key is Guid id ? clinicNames.GetValueOrDefault(id) : null,
                Count = g.Count(),
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        var recent = rows
            .Take(MaxRecentEventItems)
            .Select(r => new OrganizationSecurityEventItem
            {
                EventId = r.Id,
                EventType = r.EventType.ToString(),
                Operation = r.Operation,
                ReasonCode = r.ReasonCode,
                ClinicId = r.ClinicId,
                ActorUserId = r.ActorUserId,
                TargetUserId = r.TargetUserId,
                OccurredAtUtc = r.OccurredAtUtc,
                CorrelationId = r.CorrelationId,
            })
            .ToList();

        _audit.SecurityOperation(
            "security_" + category,
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId);

        return new OrganizationSecurityEventSummaryResponse
        {
            OrganizationId = scope.OrganizationId,
            OrganizationName = scope.OrganizationName,
            EventCategory = category,
            TotalCount = rows.Count,
            ByOperation = byOperation,
            ByClinic = byClinic,
            RecentItems = recent,
        };
    }

    private void EnsureAuthorized(string permission)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw OrganizationSecurityException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(permission);
    }

    private async Task<SecurityScope> ResolveScopeAsync(
        OrganizationSecurityQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.OrganizationId is null || query.OrganizationId == Guid.Empty)
            {
                throw OrganizationSecurityException.OrganizationScopeRequired();
            }

            var org = await _dbContext.Organizations.AsNoTracking()
                .Where(o => o.Id == query.OrganizationId.Value)
                .Select(o => new { o.Id, o.Name })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw OrganizationSecurityException.OrganizationNotFound();

            Guid? clinicId = null;
            if (query.ClinicId is Guid requested && requested != Guid.Empty)
            {
                var ok = await _dbContext.Clinics.AsNoTracking()
                    .AnyAsync(c => c.Id == requested && c.OrganizationId == org.Id, cancellationToken);
                if (!ok)
                {
                    _audit.CrossTenantDenied(
                        "organization_security_clinic",
                        OrganizationSecurityErrorCodes.ClinicNotFound,
                        org.Id,
                        requested);
                    throw OrganizationSecurityException.ClinicNotFound();
                }

                clinicId = requested;
            }

            _audit.ExplicitPlatformBypassUsed("organization_security", org.Id, clinicId);
            return new SecurityScope(org.Id, org.Name, clinicId);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw OrganizationSecurityException.AccessDenied();
        }

        if (query.OrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "organization_security_org_override",
                OrganizationSecurityErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw OrganizationSecurityException.InvalidScope();
        }

        var organizationName = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == _currentStaff.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw OrganizationSecurityException.OrganizationNotFound();

        Guid? scopedClinic = null;
        if (query.ClinicId is Guid clinicFilter && clinicFilter != Guid.Empty)
        {
            var ok = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(
                    c => c.Id == clinicFilter && c.OrganizationId == _currentStaff.OrganizationId,
                    cancellationToken);
            if (!ok)
            {
                _audit.CrossTenantDenied(
                    "organization_security_clinic",
                    OrganizationSecurityErrorCodes.ClinicNotFound,
                    _currentStaff.OrganizationId,
                    clinicFilter);
                throw OrganizationSecurityException.ClinicNotFound();
            }

            scopedClinic = clinicFilter;
        }

        return new SecurityScope(_currentStaff.OrganizationId, organizationName, scopedClinic);
    }

    private async Task<StaffMember> LoadOrgStaffAsync(
        Guid staffMemberId,
        SecurityScope scope,
        CancellationToken cancellationToken)
    {
        var staff = await _dbContext.StaffMembers.AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == staffMemberId && s.OrganizationId == scope.OrganizationId,
                cancellationToken)
            ?? throw OrganizationSecurityException.TargetNotFound();

        if (scope.ClinicId.HasValue && staff.ClinicId != scope.ClinicId.Value)
        {
            throw OrganizationSecurityException.TargetNotFound();
        }

        return staff;
    }

    private static void EnsureNotPlatformAdmin(StaffMember staff)
    {
        if (staff.Role == AppRoles.PlatformAdmin)
        {
            throw OrganizationSecurityException.PlatformAdminProtected();
        }
    }

    private async Task EnsureNotLastAdminAsync(StaffMember staff, CancellationToken cancellationToken)
    {
        if (staff.Role != AppRoles.OrganizationAdmin)
        {
            return;
        }

        var otherAdmins = await _dbContext.StaffMembers.AsNoTracking()
            .CountAsync(
                s => s.OrganizationId == staff.OrganizationId
                     && s.Role == AppRoles.OrganizationAdmin
                     && s.IsActive
                     && s.Id != staff.Id,
                cancellationToken);
        if (otherAdmins == 0)
        {
            throw OrganizationSecurityException.LastAdminProtected();
        }
    }

    private (DateTimeOffset From, DateTimeOffset To) ResolveEventRange(OrganizationSecurityQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.FromUtc) && !string.IsNullOrWhiteSpace(query.ToUtc))
        {
            if (!DateTimeOffset.TryParse(query.FromUtc, out var from)
                || !DateTimeOffset.TryParse(query.ToUtc, out var to)
                || from > to
                || (to - from).TotalDays > OrganizationSecurityQueryValidator.MaxInclusiveDays)
            {
                throw OrganizationSecurityException.InvalidDateRange();
            }

            return (from, to);
        }

        var toDefault = _timeProvider.GetUtcNow();
        return (toDefault.AddDays(-7), toDefault);
    }

    private static string? TruncateAgent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= 120 ? value : value[..120];
    }

    private sealed record SecurityScope(Guid OrganizationId, string OrganizationName, Guid? ClinicId);
}
