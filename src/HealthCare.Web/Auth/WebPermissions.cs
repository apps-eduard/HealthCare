namespace HealthCare.Web.Auth;

/// <summary>
/// UI permission names — must match API catalog. Do not recreate RolePermissionMatrix here.
/// </summary>
public static class WebPermissions
{
    public const string StaffRead = "staff.read";
    public const string StaffManage = "staff.manage";
    public const string RolesRead = "roles.read";
    public const string RolesAssign = "roles.assign";
    public const string ClinicsRead = "clinics.read";
}

public static class WebRoles
{
    public const string Patient = "PATIENT";
    public const string OrganizationAdmin = "ORGANIZATION_ADMIN";
    public const string PlatformAdmin = "PLATFORM_ADMIN";
    public const string ClinicAdmin = "CLINIC_ADMIN";
}
