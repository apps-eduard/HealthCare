using HealthCare.Application.Authorization;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;

namespace HealthCare.Infrastructure.Authorization;

/// <summary>
/// Resolves permissions from server-validated identity roles and active staff membership.
/// Does not trust client-supplied permission claims.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentPatient _currentPatient;
    private readonly IAuthorizationAuditLogger _audit;

    public PermissionService(
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        ICurrentPatient currentPatient,
        IAuthorizationAuditLogger audit)
    {
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _currentPatient = currentPatient;
        _audit = audit;
    }

    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission) || !Permissions.IsKnown(permission))
        {
            if (!string.IsNullOrWhiteSpace(permission))
            {
                _audit.UnknownPermissionRequested(permission);
            }

            return false;
        }

        if (!_currentUser.IsAuthenticated)
        {
            return false;
        }

        var roles = GetEffectiveRoles();
        return RolePermissionMatrix.GetPermissionsForRoles(roles).Contains(permission);
    }

    public bool HasAnyPermission(params string[] permissions)
    {
        if (permissions is null || permissions.Length == 0)
        {
            return false;
        }

        return permissions.Any(HasPermission);
    }

    public void RequirePermission(string permission)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (string.IsNullOrWhiteSpace(permission) || !Permissions.IsKnown(permission))
        {
            _audit.UnknownPermissionRequested(permission ?? string.Empty);
            throw AuthorizationException.InvalidPermission();
        }

        if (!HasPermission(permission))
        {
            _audit.PermissionDenied(permission, "require_permission", AuthorizationErrorCodes.PermissionDenied);
            throw AuthorizationException.PermissionDenied(permission);
        }
    }

    public IReadOnlyList<string> GetCurrentPermissions()
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Array.Empty<string>();
        }

        return RolePermissionMatrix.GetPermissionsForRoles(GetEffectiveRoles())
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<string> GetEffectiveRoles()
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Array.Empty<string>();
        }

        var roles = new HashSet<string>(_currentUser.Roles, StringComparer.Ordinal);

        // Prefer active staff membership role from DB-backed ICurrentStaff over stale JWT-only staff claims.
        if (_currentStaff.HasActiveMembership)
        {
            roles.Add(_currentStaff.Role);
        }
        else
        {
            // Inactive / missing membership: strip operational staff roles so stale JWT roles cannot authorize.
            roles.Remove(AppRoles.OrganizationAdmin);
            roles.Remove(AppRoles.ClinicAdmin);
            roles.Remove(AppRoles.Doctor);
            roles.Remove(AppRoles.Nurse);
            roles.Remove(AppRoles.Receptionist);
        }

        // PATIENT operational permissions require an active linked patient profile.
        if (!_currentPatient.HasLinkedPatient)
        {
            roles.Remove(AppRoles.Patient);
        }

        // PLATFORM_ADMIN is an Identity role (not staff membership); keep when present on the user.
        return roles.ToArray();
    }
}
