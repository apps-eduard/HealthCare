namespace HealthCare.Contracts.Identity;

/// <summary>
/// Stable authorization error codes returned via Problem Details (never include tokens or foreign tenant details).
/// </summary>
public static class AuthorizationErrorCodes
{
    public const string NotAuthenticated = "authz.not_authenticated";
    public const string InvalidCurrentUser = "authz.invalid_current_user";
    public const string AccountDisabled = "authz.account_disabled";
    public const string MissingStaffMembership = "authz.missing_staff_membership";
    public const string InactiveStaffMembership = "authz.inactive_staff_membership";
    public const string OrganizationAccessDenied = "authz.organization_access_denied";
    public const string ClinicAccessDenied = "authz.clinic_access_denied";
    public const string MissingPatientLinkage = "authz.missing_patient_linkage";
    public const string PatientSelfScopeDenied = "authz.patient_self_scope_denied";
    public const string Forbidden = "authz.forbidden";

    public const string PermissionRequired = "authorization.permission_required";
    public const string PermissionDenied = "authorization.permission_denied";
    public const string InvalidPermission = "authorization.invalid_permission";
    public const string InactiveMembership = "authorization.inactive_membership";
    public const string RoleAssignmentDenied = "authorization.role_assignment_denied";
    public const string ExplicitBypassRequired = "authorization.explicit_bypass_required";
}
