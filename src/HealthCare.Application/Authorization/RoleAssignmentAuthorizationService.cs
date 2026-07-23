using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;

namespace HealthCare.Application.Authorization;

/// <summary>
/// Deny-by-default role assignment rules for future staff-management APIs.
/// </summary>
public sealed class RoleAssignmentAuthorizationService : IRoleAssignmentAuthorizationService
{
    private readonly IAuthorizationAuditLogger _audit;

    public RoleAssignmentAuthorizationService(IAuthorizationAuditLogger audit)
    {
        _audit = audit;
    }

    public bool CanAssignRole(RoleAssignmentRequest request)
    {
        try
        {
            EnsureCanAssignRole(request);
            return true;
        }
        catch (AuthorizationException)
        {
            return false;
        }
    }

    public void EnsureCanAssignRole(RoleAssignmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ActorRole)
            || !AppRoles.All.Contains(request.ActorRole, StringComparer.Ordinal))
        {
            Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
        }

        if (string.IsNullOrWhiteSpace(request.TargetRole)
            || !AppRoles.All.Contains(request.TargetRole, StringComparer.Ordinal))
        {
            Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
        }

        if (request.ActorUserId == Guid.Empty || request.TargetUserId == Guid.Empty)
        {
            Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
        }

        // Users cannot elevate / change their own role via assignment.
        if (request.ActorUserId == request.TargetUserId)
        {
            Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
        }

        // PATIENT must not be mixed accidentally with staff membership.
        if (request.TargetRole == AppRoles.Patient && request.TargetHasStaffMembership)
        {
            Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
        }

        if (request.TargetRole != AppRoles.Patient && request.TargetHasPatientRole)
        {
            // Staff roles on a patient-linked account are rejected here for MVP safety;
            // dedicated account conversion flows can opt into a different path later.
            Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
        }

        if (!RolePermissionMatrix.RoleHasPermission(request.ActorRole, Permissions.Roles.Assign))
        {
            Deny(request, AuthorizationErrorCodes.PermissionDenied);
        }

        switch (request.ActorRole)
        {
            case AppRoles.PlatformAdmin:
                // Platform admin may assign any documented role; tenant scope still applies at API layer.
                return;

            case AppRoles.OrganizationAdmin:
                if (request.TargetRole is AppRoles.PlatformAdmin)
                {
                    Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
                }

                EnsureSameOrganization(request);
                return;

            case AppRoles.ClinicAdmin:
                if (request.TargetRole is AppRoles.PlatformAdmin or AppRoles.OrganizationAdmin)
                {
                    Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
                }

                EnsureSameClinic(request);
                return;

            default:
                Deny(request, AuthorizationErrorCodes.RoleAssignmentDenied);
                return;
        }
    }

    private static void EnsureSameOrganization(RoleAssignmentRequest request)
    {
        if (request.ActorOrganizationId is null
            || request.TargetOrganizationId is null
            || request.ActorOrganizationId != request.TargetOrganizationId)
        {
            throw AuthorizationException.RoleAssignmentDenied();
        }
    }

    private static void EnsureSameClinic(RoleAssignmentRequest request)
    {
        if (request.ActorClinicId is null
            || request.TargetClinicId is null
            || request.ActorClinicId != request.TargetClinicId)
        {
            throw AuthorizationException.RoleAssignmentDenied();
        }

        if (request.ActorOrganizationId is null
            || request.TargetOrganizationId is null
            || request.ActorOrganizationId != request.TargetOrganizationId)
        {
            throw AuthorizationException.RoleAssignmentDenied();
        }
    }

    private void Deny(RoleAssignmentRequest request, string reasonCode)
    {
        _audit.RoleAssignmentDenied(request.ActorRole, request.TargetRole, reasonCode);
        throw reasonCode switch
        {
            AuthorizationErrorCodes.PermissionDenied => AuthorizationException.PermissionDenied(Permissions.Roles.Assign),
            _ => AuthorizationException.RoleAssignmentDenied(),
        };
    }
}
