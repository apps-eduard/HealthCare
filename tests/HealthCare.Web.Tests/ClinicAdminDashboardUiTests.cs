using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminDashboardUiTests
{
    [Fact]
    public async Task Clinic_Admin_Permission_State_Has_Clinic_Dashboard()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "clinicadmin@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.ClinicDashboardRead,
                WebPermissions.StaffRead,
                WebPermissions.PatientsSearch,
                WebPermissions.AppointmentsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.ClinicDashboardRead).Should().BeTrue();
        state.Has(WebPermissions.OrganizationDashboardRead).Should().BeFalse();
        state.Has(WebPermissions.OrganizationProfileRead).Should().BeFalse();
        state.Has(WebPermissions.OrganizationUsageRead).Should().BeFalse();
        state.Has(WebPermissions.OrganizationReportsRead).Should().BeFalse();
        state.IsOrganizationAdmin.Should().BeFalse();
        state.CanFilterByClinic.Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Keeps_Organization_Dashboard_Permission()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "orgadmin@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.OrganizationDashboardRead,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.OrganizationDashboardRead).Should().BeTrue();
        state.Has(WebPermissions.ClinicDashboardRead).Should().BeFalse();
    }

    [Fact]
    public void Dashboard_Page_Branches_On_Clinic_And_Organization_Permissions()
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var dashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Dashboard.razor"));
        var clinicView = File.ReadAllText(Path.Combine(webRoot, "Components", "Dashboard", "ClinicDashboardView.razor"));

        dashboard.Should().Contain("OrganizationDashboardRead");
        dashboard.Should().Contain("ClinicDashboardRead");
        dashboard.Should().Contain("ClinicDashboardView");
        dashboard.Should().Contain("IOrganizationDashboardApiClient");

        clinicView.Should().Contain("IClinicDashboardApiClient");
        clinicView.Should().Contain("Clinic Dashboard");
        clinicView.Should().Contain("Active staff");
        clinicView.Should().Contain("Monthly appointments");
        clinicView.Should().Contain("Failed reminders");
        clinicView.Should().NotContain("MaxClinics");
        clinicView.Should().NotContain("MaxStaff");
        clinicView.Should().NotContain("Organization Profile");
        clinicView.Should().NotContain("Usage & Limits");
        clinicView.Should().NotContain("/organization/settings");
        clinicView.Should().NotContain("/usage");
        clinicView.Should().NotContain("/security");
        clinicView.Should().NotContain("/reports");
        clinicView.Should().Contain("/staff");
        clinicView.Should().Contain("/patients");
        clinicView.Should().Contain("/appointments");
    }

    [Fact]
    public void WebPermissions_Exposes_Clinic_Dashboard_Constant()
    {
        WebPermissions.ClinicDashboardRead.Should().Be("clinic_dashboard.read");
        typeof(IClinicDashboardApiClient).GetMethod(nameof(IClinicDashboardApiClient.GetAsync))
            .Should().NotBeNull();
    }

    [Fact]
    public void StaffLayout_Hides_Org_Sections_Without_Org_Permissions()
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        layout.Should().Contain("OrganizationReportsRead");
        layout.Should().Contain("GovernancePermissionRules");
        layout.Should().Contain("OrganizationSettingsPermissionRules");
        layout.Should().Contain("OrganizationSecurityPermissionRules");
    }
}
