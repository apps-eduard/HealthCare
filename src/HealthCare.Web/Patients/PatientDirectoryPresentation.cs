using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.Patients;

/// <summary>
/// Actor-aware patient directory presentation. Backend remains the security boundary.
/// Cross-clinic enroll UI stays Org/Platform Admin only; Clinic Admin manages own-clinic status.
/// </summary>
public static class PatientDirectoryPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.PatientsSearch);

    public static bool CanReadDetail(IPermissionState permissions) =>
        permissions.Has(WebPermissions.PatientsRead);

    public static bool CanUpdateClinicStatus(IPermissionState permissions) =>
        permissions.Has(WebPermissions.PatientsUpdateClinicStatus);

    /// <summary>
    /// Cross-clinic enroll dialog (pick another clinic). Not shown for Clinic Admin.
    /// </summary>
    public static bool CanEnrollAcrossClinics(IPermissionState permissions) =>
        permissions.Has(WebPermissions.PatientsUpdateClinicStatus)
        && (permissions.IsOrganizationAdmin || permissions.IsPlatformAdmin);

    public static bool ShowClinicPicker(IPermissionState permissions) =>
        permissions.CanFilterByClinic;
}

public static class PatientDirectoryPageCopy
{
    public static string Subtitle(IPermissionState permissions) =>
        permissions.IsDoctor && !permissions.IsPlatformAdmin
            ? "Patients from your assigned appointments"
            : permissions.IsClinicAdmin
            ? "Patients enrolled in your clinic"
            : permissions.IsOrganizationAdmin
                ? "Organization patient search and clinic enrollment"
                : permissions.IsPlatformAdmin
                    ? "Patients for the selected organization and clinic"
                    : "Clinic patient search and enrollment status";

    public static string ClinicCaption(IPermissionState permissions, string? clinicName) =>
        permissions.IsClinicAdmin || (permissions.IsDoctor && !permissions.IsPlatformAdmin)
            ? (string.IsNullOrWhiteSpace(clinicName) ? "Your clinic" : clinicName)
            : (clinicName ?? "Selected clinic");
}
