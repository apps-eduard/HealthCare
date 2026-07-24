using HealthCare.Web.Auth;

namespace HealthCare.Web.Staff;

/// <summary>
/// Actor-aware staff directory presentation helpers. Backend remains the security boundary.
/// </summary>
public static class StaffPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.StaffRead);

    public static bool CanManage(IPermissionState permissions) =>
        permissions.Has(WebPermissions.StaffManage);

    public static bool CanChangeClinic(IPermissionState permissions) =>
        permissions.Has(WebPermissions.StaffManage)
        && (permissions.IsOrganizationAdmin || permissions.IsPlatformAdmin);

    public static bool ShowClinicPicker(IPermissionState permissions) =>
        permissions.CanFilterByClinic;
}

public static class StaffRoleFilterOptions
{
    public static readonly string[] ClinicAdmin =
    [
        "CLINIC_ADMIN", "DOCTOR", "NURSE", "RECEPTIONIST"
    ];

    public static readonly string[] OrganizationAdmin =
    [
        "CLINIC_ADMIN", "ORGANIZATION_ADMIN", "DOCTOR", "NURSE", "RECEPTIONIST"
    ];

    public static readonly string[] PlatformAdmin =
    [
        "CLINIC_ADMIN", "ORGANIZATION_ADMIN", "DOCTOR", "NURSE", "RECEPTIONIST", "PLATFORM_ADMIN"
    ];

    public static IReadOnlyList<string> For(IPermissionState permissions)
    {
        if (permissions.IsPlatformAdmin)
        {
            return PlatformAdmin;
        }

        if (permissions.IsOrganizationAdmin)
        {
            return OrganizationAdmin;
        }

        return ClinicAdmin;
    }
}

public static class StaffPageCopy
{
    public static string Subtitle(IPermissionState permissions) =>
        permissions.IsClinicAdmin
            ? "Manage staff in your clinic — Clinic Admins and clinical roles"
            : permissions.IsPlatformAdmin
                ? "Manage staff for the selected organization and clinic context"
                : "Manage organization staff, Clinic Admins, and clinical roles";
}
