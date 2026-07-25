using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.Appointments;

/// <summary>
/// Actor-aware appointment queue/calendar presentation. Backend remains the security boundary.
/// </summary>
public static class AppointmentDirectoryPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.AppointmentsRead);

    public static bool CanCreate(IPermissionState permissions) =>
        permissions.Has(WebPermissions.AppointmentsCreate);

    public static bool CanComplete(IPermissionState permissions) =>
        permissions.Has(WebPermissions.AppointmentsComplete);

    public static bool ShowClinicPicker(IPermissionState permissions) =>
        permissions.CanFilterByClinic;
}

public static class AppointmentDirectoryPageCopy
{
    public static string QueueSubtitle(IPermissionState permissions, string? timezoneLabel) =>
        permissions.IsClinicAdmin
            ? (string.IsNullOrWhiteSpace(timezoneLabel)
                ? "Appointments for your clinic"
                : $"Your clinic · times in {timezoneLabel}")
            : permissions.IsOrganizationAdmin
                ? (string.IsNullOrWhiteSpace(timezoneLabel)
                    ? "Organization appointment queue"
                    : $"Times shown in {timezoneLabel}")
                : permissions.IsPlatformAdmin
                    ? (string.IsNullOrWhiteSpace(timezoneLabel)
                        ? "Appointments for the selected clinic"
                        : $"Selected clinic · times in {timezoneLabel}")
                    : (string.IsNullOrWhiteSpace(timezoneLabel)
                        ? "Clinic appointment queue"
                        : $"Times shown in {timezoneLabel}");

    public static string CalendarSubtitle(IPermissionState permissions, string? timezoneLabel) =>
        permissions.IsClinicAdmin
            ? (string.IsNullOrWhiteSpace(timezoneLabel)
                ? "Calendar for your clinic"
                : $"Your clinic · {timezoneLabel}")
            : (string.IsNullOrWhiteSpace(timezoneLabel)
                ? "Day and week clinic schedule"
                : $"Clinic timezone: {timezoneLabel}");

    public static string ClinicCaption(IPermissionState permissions, string? clinicName) =>
        permissions.IsClinicAdmin
            ? (string.IsNullOrWhiteSpace(clinicName) ? "Your clinic" : clinicName)
            : (clinicName ?? "Selected clinic");

    public static string CreateClinicCaption =>
        "Appointments are created in your assigned clinic.";
}
