using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.ClinicReports;
using HealthCare.Web.DoctorDashboard;

namespace HealthCare.Web.Tests;

/// <summary>
/// DR-9: Doctor / patient UI permission gates for admin surfaces that must stay hidden.
/// </summary>
public sealed class DoctorCrossRoleSecurityUiTests
{
    [Fact]
    public async Task Doctor_Cannot_View_Clinic_Reports_Or_Audit_Gates()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doctor@test.local",
            Roles = [WebRoles.Doctor],
            Permissions =
            [
                WebPermissions.DoctorDashboardRead,
                WebPermissions.AppointmentsRead,
                WebPermissions.PatientsRead,
                WebPermissions.MedicalNotesRead,
            ],
            HasActiveStaffMembership = true,
            StaffMemberId = Guid.NewGuid(),
        });

        ClinicReportsPermissionRules.CanView(state).Should().BeFalse();
        state.Has(WebPermissions.ClinicReportsRead).Should().BeFalse();
        state.Has(WebPermissions.ClinicAuditLogsRead).Should().BeFalse();
        state.Has(WebPermissions.OrganizationDashboardRead).Should().BeFalse();
        DoctorConsoleNavigation.IsDoctorConsoleActor(state).Should().BeTrue();
        DoctorConsoleNavigation.ShowOperations(state).Should().BeFalse();
        DoctorConsoleNavigation.ShowClinicsDirectory(state).Should().BeFalse();
    }

    [Fact]
    public async Task Patient_Cannot_View_Staff_Clinical_Surfaces()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "patient@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = false,
        });

        ClinicReportsPermissionRules.CanView(state).Should().BeFalse();
        DoctorDashboardPermissionRules.CanView(state).Should().BeFalse();
        state.Has(WebPermissions.MedicalNotesRead).Should().BeFalse();
        state.Has(WebPermissions.StaffRead).Should().BeFalse();
    }

    [Fact]
    public void StaffLayout_Gates_Reports_And_Audit_Behind_Permissions()
    {
        var layout = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HealthCare.Web", "Components", "Layout", "StaffLayout.razor"));
        layout.Should().Contain("ClinicReportsPermissionRules.CanView");
        layout.Should().Contain("/clinic/reports");
        layout.Should().Contain("/clinic/audit-logs");
        layout.Should().Contain("DoctorConsoleNavigation.ShowOperations");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HealthCare.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
