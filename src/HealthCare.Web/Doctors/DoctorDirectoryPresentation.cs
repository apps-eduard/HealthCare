using HealthCare.Contracts.Staff;
using HealthCare.Web.Auth;
using HealthCare.Web.Availability;
using HealthCare.Web.Services;

namespace HealthCare.Web.Doctors;

/// <summary>
/// Actor-aware doctor directory presentation. Activation and account controls stay on /staff.
/// </summary>
public static class DoctorDirectoryPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.StaffRead)
        || AvailabilityPermissionRules.CanView(permissions);

    public static bool UseStaffDirectory(IPermissionState permissions) =>
        permissions.Has(WebPermissions.StaffRead);

    public static bool ShowClinicPicker(IPermissionState permissions) =>
        permissions.CanFilterByClinic;

    public static bool CanOpenAvailability(IPermissionState permissions) =>
        AvailabilityPermissionRules.CanView(permissions);

    public static bool CanOpenAppointments(IPermissionState permissions) =>
        permissions.Has(WebPermissions.AppointmentsRead);

    public static bool CanOpenStaffAccount(IPermissionState permissions) =>
        permissions.Has(WebPermissions.StaffRead);
}

public static class DoctorDirectoryPageCopy
{
    public static string Subtitle(IPermissionState permissions) =>
        permissions.IsClinicAdmin
            ? "Doctors in your clinic — schedules and appointments"
            : permissions.IsOrganizationAdmin
                ? "Organization doctors for scheduling and availability"
                : permissions.IsPlatformAdmin
                    ? "Doctors for the selected organization and clinic"
                    : "Clinic doctors for scheduling and availability";

    public static string ClinicCaption(IPermissionState permissions, string? clinicName) =>
        permissions.IsClinicAdmin
            ? (string.IsNullOrWhiteSpace(clinicName) ? "Your clinic" : clinicName)
            : (clinicName ?? "Selected clinic");
}

public static class DoctorDirectoryDisplay
{
    public static string Name(StaffSummaryResponse row)
    {
        if (!string.IsNullOrWhiteSpace(row.DisplayName))
        {
            return row.DisplayName.Trim();
        }

        var composed = $"{row.FirstName} {row.LastName}".Trim();
        return string.IsNullOrWhiteSpace(composed) ? (row.JobTitle ?? "Doctor") : composed;
    }

    public static string AvailabilityHref(Guid staffMemberId) =>
        $"/availability?doctorId={staffMemberId:D}";

    public static string AppointmentsHref(Guid staffMemberId) =>
        $"/appointments?doctorId={staffMemberId:D}";
}

public static class DoctorDirectoryProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.StatusCode switch
        {
            400 => "The request was invalid. Check the fields and try again.",
            401 => "Your session expired. Sign in again.",
            403 => "You do not have permission to view doctors for this clinic.",
            404 => "Doctor was not found or is outside your clinic scope.",
            409 => "This record was updated by someone else. Reload and try again.",
            _ => ex.ToUserMessage(),
        };
    }
}
