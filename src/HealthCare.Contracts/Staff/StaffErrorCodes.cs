namespace HealthCare.Contracts.Staff;

public static class StaffErrorCodes
{
    public const string NotFound = "staff.not_found";
    public const string EmailInUse = "staff.email_in_use";
    public const string InvalidRole = "staff.invalid_role";
    public const string RoleAssignmentDenied = "staff.role_assignment_denied";
    public const string SelfElevationDenied = "staff.self_elevation_denied";
    public const string CrossTenantDenied = "staff.cross_tenant_denied";
    public const string InactiveOrganization = "staff.inactive_organization";
    public const string InactiveClinic = "staff.inactive_clinic";
    public const string ConcurrencyConflict = "staff.concurrency_conflict";
    public const string LastAdminProtected = "staff.last_admin_protected";
    public const string SessionRevocationFailed = "staff.session_revocation_failed";
    public const string ActivationNotAllowed = "staff.activation_not_allowed";
    public const string DeactivationNotAllowed = "staff.deactivation_not_allowed";
    public const string AlreadyActive = "staff.already_active";
    public const string AlreadyInactive = "staff.already_inactive";
    public const string EmptyPatch = "staff.empty_patch";
    public const string CreationFailed = "staff.creation_failed";
}
