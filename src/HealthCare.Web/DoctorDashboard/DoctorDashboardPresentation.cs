using HealthCare.Contracts.Doctors;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.DoctorDashboard;

public static class DoctorDashboardPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.DoctorDashboardRead);
}

/// <summary>
/// Doctor console navigation helpers. Patients remain hidden until DR-5 Model A ships.
/// Appointments remain visible; ownership hardening is DR-4.
/// </summary>
public static class DoctorConsoleNavigation
{
    public static bool IsDoctorConsoleActor(IPermissionState permissions) =>
        permissions.IsDoctor
        && permissions.Has(WebPermissions.DoctorDashboardRead);

    public static bool ShowMyProfileLink(IPermissionState permissions) =>
        IsDoctorConsoleActor(permissions) && permissions.Has(WebPermissions.DoctorProfileRead);

    public static bool ShowPatientsLink(IPermissionState permissions) =>
        !IsDoctorConsoleActor(permissions) && permissions.Has(WebPermissions.PatientsSearch);

    public static bool ShowDoctorsDirectory(IPermissionState permissions) =>
        !IsDoctorConsoleActor(permissions);

    public static bool ShowOperations(IPermissionState permissions) =>
        !IsDoctorConsoleActor(permissions);

    public static bool ShowClinicsDirectory(IPermissionState permissions) =>
        !IsDoctorConsoleActor(permissions) && permissions.Has(WebPermissions.ClinicsRead);
}

public static class DoctorDashboardProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            DoctorDashboardErrorCodes.AccessDenied =>
                "You do not have permission to view the doctor dashboard.",
            DoctorDashboardErrorCodes.InvalidScope =>
                "The selected doctor dashboard scope is invalid.",
            DoctorDashboardErrorCodes.ClinicScopeRequired =>
                "Select a clinic before loading the doctor dashboard.",
            DoctorDashboardErrorCodes.DoctorScopeRequired =>
                "Select a doctor before loading the doctor dashboard.",
            DoctorDashboardErrorCodes.ClinicNotFound =>
                "Clinic was not found.",
            DoctorDashboardErrorCodes.DoctorNotFound =>
                "Doctor was not found.",
            DoctorDashboardErrorCodes.InvalidDate =>
                "The dashboard date is invalid.",
            "authorization.permission_denied" =>
                "You do not have permission to view the doctor dashboard.",
            _ => ex.StatusCode switch
            {
                401 => "Sign in to view the doctor dashboard.",
                403 => "You do not have permission to view the doctor dashboard.",
                404 => "Doctor dashboard context was not found.",
                _ => string.IsNullOrWhiteSpace(ex.Title)
                    ? "Unable to load doctor dashboard."
                    : ex.Title,
            },
        };
    }
}
