using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.ClinicReports;

public static class ClinicReportsPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.ClinicReportsRead);
}

public static class ClinicReportsPageCopy
{
    public const int MaxInclusiveDays = 93;

    public static string Subtitle(IPermissionState permissions) =>
        permissions.IsClinicAdmin
            ? "Operational aggregates for your clinic (JSON only — no export)"
            : permissions.IsPlatformAdmin
                ? "Operational aggregates for the selected clinic (JSON only — no export)"
                : "Clinic operational reports";

    public static string ClinicCaption(IPermissionState permissions, string? clinicName) =>
        permissions.IsClinicAdmin
            ? (string.IsNullOrWhiteSpace(clinicName) ? "Your clinic" : clinicName)
            : (clinicName ?? "Selected clinic");
}

public static class ClinicReportProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "clinic_reports.invalid_date_range" =>
                "Choose a valid date range of at most 93 inclusive days.",
            "clinic_reports.clinic_scope_required" =>
                "Select a clinic before loading clinic reports.",
            "clinic_reports.clinic_not_found" =>
                "The clinic was not found or you do not have access.",
            "clinic_reports.invalid_scope" =>
                "That clinic is outside your authorized scope.",
            "clinic_reports.access_denied" or "authorization.permission_denied" =>
                "You do not have permission to view clinic reports.",
            _ => ex.ToUserMessage(),
        };
    }
}

public static class ClinicReportViewKeys
{
    public const string Appointments = "appointments";
    public const string Doctors = "doctors";
    public const string Patients = "patients";
    public const string Operations = "operations";
}
