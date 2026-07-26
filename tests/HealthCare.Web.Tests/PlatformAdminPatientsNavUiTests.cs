using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.DoctorDashboard;

namespace HealthCare.Web.Tests;

/// <summary>
/// Regression: PLATFORM_ADMIN may see administrative Patient Directory nav when granted patients.search.
/// This is not Patient self-service (/patients/me).
/// </summary>
public sealed class PlatformAdminPatientsNavUiTests
{
    [Fact]
    public async Task Platform_Admin_With_PatientsSearch_Sees_Patients_Link()
    {
        var state = await PlatformAdminStateAsync(includePatientsSearch: true);

        DoctorConsoleNavigation.ShowPatientsLink(state).Should().BeTrue();
        state.Has(WebPermissions.PatientsSearch).Should().BeTrue();
        state.IsPlatformAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Platform_Admin_Without_PatientsSearch_Hides_Patients_Link()
    {
        var state = await PlatformAdminStateAsync(includePatientsSearch: false);

        DoctorConsoleNavigation.ShowPatientsLink(state).Should().BeFalse();
        state.Has(WebPermissions.PatientsSearch).Should().BeFalse();
    }

    [Fact]
    public void StaffLayout_Patients_Link_Is_Permission_Driven_And_Routes_To_Patients()
    {
        var layout = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Components", "Layout", "StaffLayout.razor"));

        layout.Should().Contain("DoctorConsoleNavigation.ShowPatientsLink");
        layout.Should().Contain("RouterLink=\"/patients\"");
        layout.Should().NotContain("RouterLink=\"/patients/me\"");
        layout.Should().Contain("Key=\"patients\"");
    }

    [Fact]
    public void Patients_Page_Is_Staff_Directory_Not_Self_Service()
    {
        var page = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Components", "Pages", "Patients.razor"));

        page.Should().Contain("@page \"/patients\"");
        page.Should().Contain("Patient Directory");
        page.Should().NotContain("/patients/me");
        page.Should().NotContain("MedicalNote");
        page.Should().NotContain("medical_notes");
    }

    private static async Task<PermissionState> PlatformAdminStateAsync(bool includePatientsSearch)
    {
        var permissions = new List<string>
        {
            WebPermissions.OrganizationsRead,
            WebPermissions.OrganizationsSelect,
            WebPermissions.ClinicsRead,
            WebPermissions.PatientsRead,
        };
        if (includePatientsSearch)
        {
            permissions.Add(WebPermissions.PatientsSearch);
        }

        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions = permissions,
            HasActiveStaffMembership = false,
        });
        return state;
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
