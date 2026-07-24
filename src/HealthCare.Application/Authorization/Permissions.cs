namespace HealthCare.Application.Authorization;

/// <summary>
/// Immutable permission catalog. Permission strings are stable identifiers used by policies and the role matrix.
/// Unknown permissions must never grant access.
/// </summary>
public static class Permissions
{
    public const string PolicyPrefix = "perm:";

    public static class Patients
    {
        public const string Read = "patients.read";
        public const string Search = "patients.search";
        public const string UpdateClinicStatus = "patients.update_clinic_status";
        public const string UpdateOwnProfile = "patients.update_own_profile";
    }

    public static class Appointments
    {
        public const string Read = "appointments.read";
        public const string Create = "appointments.create";
        public const string Confirm = "appointments.confirm";
        public const string Cancel = "appointments.cancel";
        public const string CheckIn = "appointments.check_in";
        public const string Complete = "appointments.complete";
        public const string NoShow = "appointments.no_show";
        public const string Reschedule = "appointments.reschedule";
    }

    public static class Availability
    {
        public const string Read = "availability.read";
        public const string ManageSelf = "availability.manage_self";
        public const string ManageClinic = "availability.manage_clinic";
        public const string ManageOrganization = "availability.manage_organization";
    }

    public static class Reminders
    {
        public const string Read = "reminders.read";
        public const string Retry = "reminders.retry";
    }

    public static class Summaries
    {
        public const string Read = "summaries.read";
        public const string Retry = "summaries.retry";
    }

    public static class Clinics
    {
        public const string Read = "clinics.read";
        public const string Manage = "clinics.manage";
        public const string Create = "clinics.create";
        public const string Update = "clinics.update";
        public const string Activate = "clinics.activate";
        public const string Deactivate = "clinics.deactivate";
    }

    public static class Organizations
    {
        public const string Read = "organizations.read";
        public const string Select = "organizations.select";
        public const string DashboardRead = "organization_dashboard.read";
        public const string ReportsRead = "organization_reports.read";
    }

    public static class Staff
    {
        public const string Read = "staff.read";
        public const string Manage = "staff.manage";
        public const string PasswordReset = "staff.password_reset";
    }

    public static class Roles
    {
        public const string Read = "roles.read";
        public const string Assign = "roles.assign";
    }

    public static class SecuritySessions
    {
        public const string Revoke = "security_sessions.revoke";
    }

    public static class Hangfire
    {
        public const string Dashboard = "hangfire.dashboard";
    }

    public static class MedicalNotes
    {
        public const string Read = "medical_notes.read";
        public const string Create = "medical_notes.create";
        public const string UpdateDraft = "medical_notes.update_draft";
        public const string Sign = "medical_notes.sign";
        public const string Amend = "medical_notes.amend";
    }

    /// <summary>Every known permission constant (unique).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Patients.Read,
        Patients.Search,
        Patients.UpdateClinicStatus,
        Patients.UpdateOwnProfile,
        Appointments.Read,
        Appointments.Create,
        Appointments.Confirm,
        Appointments.Cancel,
        Appointments.CheckIn,
        Appointments.Complete,
        Appointments.NoShow,
        Appointments.Reschedule,
        Availability.Read,
        Availability.ManageSelf,
        Availability.ManageClinic,
        Availability.ManageOrganization,
        Reminders.Read,
        Reminders.Retry,
        Summaries.Read,
        Summaries.Retry,
        Clinics.Read,
        Clinics.Manage,
        Clinics.Create,
        Clinics.Update,
        Clinics.Activate,
        Clinics.Deactivate,
        Organizations.Read,
        Organizations.Select,
        Organizations.DashboardRead,
        Organizations.ReportsRead,
        Staff.Read,
        Staff.Manage,
        Staff.PasswordReset,
        Roles.Read,
        Roles.Assign,
        SecuritySessions.Revoke,
        Hangfire.Dashboard,
        MedicalNotes.Read,
        MedicalNotes.Create,
        MedicalNotes.UpdateDraft,
        MedicalNotes.Sign,
        MedicalNotes.Amend,
    ];

    public static bool IsKnown(string permission) =>
        !string.IsNullOrWhiteSpace(permission)
        && All.Contains(permission, StringComparer.Ordinal);

    public static string ToPolicyName(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return PolicyPrefix + permission;
    }

    public static bool TryParsePolicyName(string? policyName, out string permission)
    {
        permission = string.Empty;
        if (string.IsNullOrWhiteSpace(policyName)
            || !policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        permission = policyName[PolicyPrefix.Length..];
        return permission.Length > 0;
    }
}
