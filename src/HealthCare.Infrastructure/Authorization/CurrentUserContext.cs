using System.Security.Claims;
using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Authorization;

/// <summary>
/// Request-scoped identity context. Claims provide the authenticated principal;
/// organization/clinic/staff scope is re-validated from the database.
/// </summary>
public sealed class CurrentUserContext : ICurrentUser, ICurrentStaff, ICurrentPatient
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly HealthCareDbContext _dbContext;
    private readonly ILogger<CurrentUserContext> _logger;
    private bool _loaded;
    private bool _isAuthenticated;
    private Guid? _userId;
    private string? _email;
    private IReadOnlyList<string> _roles = Array.Empty<string>();
    private Guid? _organizationId;
    private Guid? _clinicId;
    private Guid? _patientId;
    private Guid? _staffMemberId;
    private string? _staffRole;
    private bool _hasActiveStaffMembership;
    private bool _userDisabled;

    public CurrentUserContext(
        IHttpContextAccessor httpContextAccessor,
        HealthCareDbContext dbContext,
        ILogger<CurrentUserContext> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _logger = logger;
    }

    public bool IsAuthenticated
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled;
        }
    }

    public Guid? UserId
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _userId : null;
        }
    }

    public string? Email
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _email : null;
        }
    }

    public IReadOnlyList<string> Roles
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _roles : Array.Empty<string>();
        }
    }

    public Guid? OrganizationId
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _organizationId : null;
        }
    }

    public Guid? ClinicId
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _clinicId : null;
        }
    }

    public Guid? PatientId
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _patientId : null;
        }
    }

    public Guid? StaffMemberId
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _staffMemberId : null;
        }
    }

    bool ICurrentStaff.HasActiveMembership
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled && _hasActiveStaffMembership;
        }
    }

    Guid ICurrentStaff.StaffMemberId
    {
        get
        {
            EnsureLoaded();
            if (!_hasActiveStaffMembership || _staffMemberId is null)
            {
                throw AuthorizationException.MissingStaffMembership();
            }

            return _staffMemberId.Value;
        }
    }

    Guid ICurrentStaff.OrganizationId
    {
        get
        {
            EnsureLoaded();
            if (!_hasActiveStaffMembership || _organizationId is null)
            {
                throw AuthorizationException.MissingStaffMembership();
            }

            return _organizationId.Value;
        }
    }

    Guid ICurrentStaff.ClinicId
    {
        get
        {
            EnsureLoaded();
            if (!_hasActiveStaffMembership || _clinicId is null)
            {
                throw AuthorizationException.MissingStaffMembership();
            }

            return _clinicId.Value;
        }
    }

    string ICurrentStaff.Role
    {
        get
        {
            EnsureLoaded();
            if (!_hasActiveStaffMembership || string.IsNullOrWhiteSpace(_staffRole))
            {
                throw AuthorizationException.MissingStaffMembership();
            }

            return _staffRole;
        }
    }

    bool ICurrentPatient.HasLinkedPatient
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled && _patientId.HasValue;
        }
    }

    Guid? ICurrentPatient.PatientId
    {
        get
        {
            EnsureLoaded();
            return _isAuthenticated && !_userDisabled ? _patientId : null;
        }
    }

    public bool IsInRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return Roles.Contains(role, StringComparer.Ordinal);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        var httpContext = _httpContextAccessor.HttpContext;
        var principal = httpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            _logger.LogWarning("Authenticated principal is missing a valid user id claim");
            return;
        }

        // Synchronous load is intentional for request-scoped accessors used by sync policy handlers.
        // Call sites that can await should prefer explicit async APIs in later phases.
        var user = _dbContext.Users.AsNoTracking().SingleOrDefault(u => u.Id == userId);
        if (user is null)
        {
            _logger.LogWarning("Authenticated user {UserId} was not found", userId);
            return;
        }

        if (!user.IsActive)
        {
            _userDisabled = true;
            _userId = userId;
            _logger.LogInformation("Authenticated user {UserId} is disabled", userId);
            return;
        }

        _isAuthenticated = true;
        _userId = user.Id;
        _email = user.Email;

        // Prefer Identity role assignments from the database so revoked roles take effect immediately
        // (JWT role claims alone are not authoritative for authorization).
        // Two-step query keeps InMemory and relational providers happy.
        var roleIds = _dbContext.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToList();
        _roles = roleIds.Count == 0
            ? Array.Empty<string>()
            : _dbContext.Roles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .ToList()
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => r.Name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        // Resolve PatientId only from the server-side Patient↔User linkage.
        // Ignore any client- or claim-supplied patient id unless it matches the DB link.
        var linkedPatient = _dbContext.Patients
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.IsActive)
            .Select(p => new { p.Id })
            .SingleOrDefault();
        _patientId = linkedPatient?.Id;

        var staffMatches = _dbContext.StaffMembers
            .AsNoTracking()
            .Include(s => s.Organization)
            .Include(s => s.Clinic)
            .Where(s => s.UserId == userId)
            .ToList();

        if (staffMatches.Count > 1)
        {
            throw new InvalidOperationException(
                "Multiple staff memberships found for the authenticated user. MVP supports one membership per user.");
        }

        var staff = staffMatches.SingleOrDefault();
        if (staff is null)
        {
            return;
        }

        if (!staff.IsActive)
        {
            _logger.LogInformation(
                "Staff membership {StaffMemberId} for user {UserId} is inactive",
                staff.Id,
                userId);
            return;
        }

        if (staff.Organization is null || staff.Organization.Status != OrganizationStatus.Active)
        {
            _logger.LogInformation(
                "Staff membership {StaffMemberId} organization is inactive",
                staff.Id);
            return;
        }

        if (staff.Clinic is null || !staff.Clinic.IsActive)
        {
            _logger.LogInformation(
                "Staff membership {StaffMemberId} clinic is inactive",
                staff.Id);
            return;
        }

        // Prefer server-side membership over JWT tenant claims.
        _hasActiveStaffMembership = true;
        _staffMemberId = staff.Id;
        _organizationId = staff.OrganizationId;
        _clinicId = staff.ClinicId;
        _staffRole = staff.Role;
    }
}
