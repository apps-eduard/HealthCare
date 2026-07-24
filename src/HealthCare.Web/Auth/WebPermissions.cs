namespace HealthCare.Web.Auth;

/// <summary>
/// UI permission names — must match API catalog. Do not recreate RolePermissionMatrix here.
/// </summary>
public static class WebPermissions
{
    public const string StaffRead = "staff.read";
    public const string StaffManage = "staff.manage";
    public const string StaffPasswordReset = "staff.password_reset";
    public const string RolesRead = "roles.read";
    public const string RolesAssign = "roles.assign";
    public const string SecuritySessionsRevoke = "security_sessions.revoke";
    public const string ClinicsRead = "clinics.read";
    public const string ClinicsManage = "clinics.manage";
    public const string ClinicsCreate = "clinics.create";
    public const string ClinicsUpdate = "clinics.update";
    public const string ClinicsActivate = "clinics.activate";
    public const string ClinicsDeactivate = "clinics.deactivate";
    public const string OrganizationsRead = "organizations.read";
    public const string OrganizationsSelect = "organizations.select";
    public const string OrganizationDashboardRead = "organization_dashboard.read";

    public const string AppointmentsRead = "appointments.read";
    public const string AppointmentsCreate = "appointments.create";
    public const string AppointmentsConfirm = "appointments.confirm";
    public const string AppointmentsCheckIn = "appointments.check_in";
    public const string AppointmentsComplete = "appointments.complete";
    public const string AppointmentsNoShow = "appointments.no_show";
    public const string AppointmentsCancel = "appointments.cancel";
    public const string AppointmentsReschedule = "appointments.reschedule";

    public const string AvailabilityRead = "availability.read";
    public const string AvailabilityManageSelf = "availability.manage_self";
    public const string AvailabilityManageClinic = "availability.manage_clinic";
    public const string AvailabilityManageOrganization = "availability.manage_organization";
    public const string PatientsSearch = "patients.search";
    public const string PatientsRead = "patients.read";
    public const string PatientsUpdateClinicStatus = "patients.update_clinic_status";

    public const string RemindersRead = "reminders.read";
    public const string RemindersRetry = "reminders.retry";
    public const string SummariesRead = "summaries.read";
    public const string SummariesRetry = "summaries.retry";
}

public static class WebRoles
{
    public const string Patient = "PATIENT";
    public const string OrganizationAdmin = "ORGANIZATION_ADMIN";
    public const string PlatformAdmin = "PLATFORM_ADMIN";
    public const string ClinicAdmin = "CLINIC_ADMIN";
}
