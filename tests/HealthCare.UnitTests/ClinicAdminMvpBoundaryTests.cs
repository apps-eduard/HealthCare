using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;

namespace HealthCare.UnitTests;

/// <summary>
/// CA-10 regression: Clinic Admin permission boundary (approved operational grants only).
/// </summary>
public sealed class ClinicAdminMvpBoundaryTests
{
    [Fact]
    public void Clinic_Admin_Has_Approved_Operational_Permissions()
    {
        var permissions = RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin);

        permissions.Should().Contain(new[]
        {
            Permissions.Clinics.DashboardRead,
            Permissions.Clinics.ProfileRead,
            Permissions.Clinics.ProfileUpdate,
            Permissions.Clinics.ReportsRead,
            Permissions.Clinics.AuditLogsRead,
            Permissions.Clinics.Read,
            Permissions.Staff.Read,
            Permissions.Staff.Manage,
            Permissions.Staff.PasswordReset,
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
            Permissions.Roles.Read,
            Permissions.Roles.Assign,
            Permissions.SecuritySessions.Revoke,
        });
    }

    [Fact]
    public void Clinic_Admin_Does_Not_Receive_Organization_Platform_Or_Clinical_Note_Permissions()
    {
        var permissions = RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin);

        permissions.Should().NotContain(new[]
        {
            Permissions.Organizations.DashboardRead,
            Permissions.Organizations.ReportsRead,
            Permissions.Organizations.AuditLogsRead,
            Permissions.Organizations.UsageRead,
            Permissions.Organizations.ProfileRead,
            Permissions.Organizations.ProfileUpdate,
            Permissions.Organizations.Read,
            Permissions.Organizations.Select,
            Permissions.Clinics.Create,
            Permissions.Clinics.Update,
            Permissions.Clinics.Activate,
            Permissions.Clinics.Deactivate,
            Permissions.Clinics.Manage,
            Permissions.Availability.ManageOrganization,
            Permissions.SecuritySessions.Read,
            Permissions.Hangfire.Dashboard,
            Permissions.MedicalNotes.Read,
            Permissions.MedicalNotes.Create,
            Permissions.MedicalNotes.UpdateDraft,
            Permissions.MedicalNotes.Sign,
            Permissions.MedicalNotes.Amend,
        });

        permissions.Should().NotContain(p =>
            p.StartsWith("billing.", StringComparison.Ordinal)
            || p.StartsWith("subscription.", StringComparison.Ordinal)
            || p.StartsWith("platform.", StringComparison.Ordinal)
            || p.StartsWith("organization_security.", StringComparison.Ordinal)
            || p.StartsWith("organization_limits.", StringComparison.Ordinal));
    }

    [Fact]
    public void Clinic_Admin_Permission_Count_Stays_Bounded_To_Mvp_Set()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().HaveCount(29);
    }
}
