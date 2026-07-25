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

    /// <summary>
    /// Doctor schedule UX locks the doctor filter to the authenticated membership.
    /// Presentation only — appointment API ownership hardens in DR-4.
    /// </summary>
    public static bool LockDoctorFilterToSelf(IPermissionState permissions) =>
        permissions.IsDoctor && !permissions.IsPlatformAdmin;
}

public static class AppointmentDirectoryPageCopy
{
    public static string QueueSubtitle(IPermissionState permissions, string? timezoneLabel)
    {
        if (AppointmentDirectoryPermissionRules.LockDoctorFilterToSelf(permissions))
        {
            return string.IsNullOrWhiteSpace(timezoneLabel)
                ? "Your assigned appointments"
                : $"Your assigned appointments · times in {timezoneLabel}";
        }

        return permissions.IsClinicAdmin
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
    }

    public static string CalendarSubtitle(IPermissionState permissions, string? timezoneLabel)
    {
        if (AppointmentDirectoryPermissionRules.LockDoctorFilterToSelf(permissions))
        {
            return string.IsNullOrWhiteSpace(timezoneLabel)
                ? "Your assigned schedule"
                : $"Your assigned schedule · {timezoneLabel}";
        }

        return permissions.IsClinicAdmin
            ? (string.IsNullOrWhiteSpace(timezoneLabel)
                ? "Calendar for your clinic"
                : $"Your clinic · {timezoneLabel}")
            : (string.IsNullOrWhiteSpace(timezoneLabel)
                ? "Day and week clinic schedule"
                : $"Clinic timezone: {timezoneLabel}");
    }

    public static string ClinicCaption(IPermissionState permissions, string? clinicName) =>
        permissions.IsClinicAdmin || AppointmentDirectoryPermissionRules.LockDoctorFilterToSelf(permissions)
            ? (string.IsNullOrWhiteSpace(clinicName) ? "Your clinic" : clinicName)
            : (clinicName ?? "Selected clinic");

    public static string CreateClinicCaption =>
        "Appointments are created in your assigned clinic.";
}
