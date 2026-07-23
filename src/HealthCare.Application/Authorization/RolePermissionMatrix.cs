using HealthCare.Domain.Identity;

namespace HealthCare.Application.Authorization;

/// <summary>
/// Code-defined role → permission matrix for the MVP.
/// Assumptions are documented in Docs/authorization-matrix.md.
/// Custom DB-editable roles are deferred.
/// </summary>
public static class RolePermissionMatrix
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [AppRoles.PlatformAdmin] = Set(
                Permissions.Patients.Read,
                Permissions.Patients.Search,
                Permissions.Patients.UpdateClinicStatus,
                Permissions.Patients.UpdateOwnProfile,
                Permissions.Appointments.Read,
                Permissions.Appointments.Create,
                Permissions.Appointments.Confirm,
                Permissions.Appointments.Cancel,
                Permissions.Appointments.CheckIn,
                Permissions.Appointments.Complete,
                Permissions.Appointments.NoShow,
                Permissions.Appointments.Reschedule,
                Permissions.Availability.Read,
                Permissions.Availability.ManageSelf,
                Permissions.Availability.ManageClinic,
                Permissions.Availability.ManageOrganization,
                Permissions.Reminders.Read,
                Permissions.Reminders.Retry,
                Permissions.Summaries.Read,
                Permissions.Summaries.Retry,
                Permissions.Clinics.Read,
                Permissions.Clinics.Manage,
                Permissions.Staff.Read,
                Permissions.Staff.Manage,
                Permissions.Roles.Read,
                Permissions.Roles.Assign,
                Permissions.Hangfire.Dashboard),

            [AppRoles.OrganizationAdmin] = Set(
                Permissions.Patients.Read,
                Permissions.Patients.Search,
                Permissions.Patients.UpdateClinicStatus,
                Permissions.Appointments.Read,
                Permissions.Appointments.Create,
                Permissions.Appointments.Confirm,
                Permissions.Appointments.Cancel,
                Permissions.Appointments.CheckIn,
                Permissions.Appointments.Complete,
                Permissions.Appointments.NoShow,
                Permissions.Appointments.Reschedule,
                Permissions.Availability.Read,
                Permissions.Availability.ManageClinic,
                Permissions.Availability.ManageOrganization,
                Permissions.Reminders.Read,
                Permissions.Reminders.Retry,
                Permissions.Summaries.Read,
                Permissions.Summaries.Retry,
                Permissions.Clinics.Read,
                Permissions.Clinics.Manage,
                Permissions.Staff.Read,
                Permissions.Staff.Manage,
                Permissions.Roles.Read,
                Permissions.Roles.Assign),

            [AppRoles.ClinicAdmin] = Set(
                Permissions.Patients.Read,
                Permissions.Patients.Search,
                Permissions.Patients.UpdateClinicStatus,
                Permissions.Appointments.Read,
                Permissions.Appointments.Create,
                Permissions.Appointments.Confirm,
                Permissions.Appointments.Cancel,
                Permissions.Appointments.CheckIn,
                Permissions.Appointments.Complete,
                Permissions.Appointments.NoShow,
                Permissions.Appointments.Reschedule,
                Permissions.Availability.Read,
                Permissions.Availability.ManageClinic,
                Permissions.Reminders.Read,
                Permissions.Reminders.Retry,
                Permissions.Summaries.Read,
                Permissions.Summaries.Retry,
                Permissions.Clinics.Read,
                Permissions.Clinics.Manage,
                Permissions.Staff.Read,
                Permissions.Staff.Manage,
                Permissions.Roles.Read,
                Permissions.Roles.Assign),

            [AppRoles.Doctor] = Set(
                Permissions.Patients.Read,
                Permissions.Patients.Search,
                Permissions.Appointments.Read,
                Permissions.Appointments.Create,
                Permissions.Appointments.Confirm,
                Permissions.Appointments.Cancel,
                Permissions.Appointments.CheckIn,
                Permissions.Appointments.Complete,
                Permissions.Appointments.NoShow,
                Permissions.Appointments.Reschedule,
                Permissions.Availability.Read,
                Permissions.Availability.ManageSelf,
                Permissions.Reminders.Read,
                Permissions.Reminders.Retry,
                Permissions.Summaries.Read,
                Permissions.Clinics.Read,
                Permissions.MedicalNotes.Read,
                Permissions.MedicalNotes.Create,
                Permissions.MedicalNotes.UpdateDraft,
                Permissions.MedicalNotes.Sign,
                Permissions.MedicalNotes.Amend),

            [AppRoles.Nurse] = Set(
                Permissions.Patients.Read,
                Permissions.Patients.Search,
                Permissions.Appointments.Read,
                Permissions.Appointments.Confirm,
                Permissions.Appointments.Cancel,
                Permissions.Appointments.CheckIn,
                Permissions.Appointments.Complete,
                Permissions.Appointments.NoShow,
                Permissions.Availability.Read,
                Permissions.Reminders.Read,
                Permissions.Clinics.Read,
                Permissions.MedicalNotes.Read,
                Permissions.MedicalNotes.Create,
                Permissions.MedicalNotes.UpdateDraft,
                Permissions.MedicalNotes.Sign),

            [AppRoles.Receptionist] = Set(
                Permissions.Patients.Read,
                Permissions.Patients.Search,
                Permissions.Patients.UpdateClinicStatus,
                Permissions.Appointments.Read,
                Permissions.Appointments.Create,
                Permissions.Appointments.Confirm,
                Permissions.Appointments.Cancel,
                Permissions.Appointments.CheckIn,
                Permissions.Appointments.Reschedule,
                Permissions.Availability.Read,
                Permissions.Reminders.Read,
                Permissions.Clinics.Read),

            [AppRoles.Patient] = Set(
                Permissions.Patients.Read,
                Permissions.Patients.UpdateOwnProfile,
                Permissions.Appointments.Read,
                Permissions.Appointments.Create,
                Permissions.Appointments.Cancel,
                Permissions.Appointments.Reschedule,
                Permissions.Availability.Read,
                Permissions.Clinics.Read),
        };

    public static IReadOnlySet<string> GetPermissionsForRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return Empty;
        }

        return Map.TryGetValue(role, out var permissions) ? permissions : Empty;
    }

    public static IReadOnlySet<string> GetPermissionsForRoles(IEnumerable<string> roles)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            foreach (var permission in GetPermissionsForRole(role))
            {
                result.Add(permission);
            }
        }

        return result;
    }

    public static bool RoleHasPermission(string role, string permission) =>
        GetPermissionsForRole(role).Contains(permission);

    private static IReadOnlySet<string> Set(params string[] permissions) =>
        new HashSet<string>(permissions, StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> Empty =
        new HashSet<string>(StringComparer.Ordinal);
}
