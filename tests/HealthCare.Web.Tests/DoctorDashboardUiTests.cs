using FluentAssertions;
using HealthCare.Contracts.Doctors;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.DoctorDashboard;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class DoctorDashboardUiTests
{
    [Fact]
    public async Task Doctor_Permission_State_Has_Doctor_Dashboard()
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
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageSelf,
                WebPermissions.PatientsSearch,
                WebPermissions.ClinicsRead,
                WebPermissions.RemindersRead,
                WebPermissions.SummariesRead,
            ],
            HasActiveStaffMembership = true,
            ClinicId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            StaffMemberId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.DoctorDashboardRead).Should().BeTrue();
        state.IsDoctor.Should().BeTrue();
        DoctorDashboardPermissionRules.CanView(state).Should().BeTrue();
        DoctorConsoleNavigation.IsDoctorConsoleActor(state).Should().BeTrue();
        DoctorConsoleNavigation.ShowPatientsLink(state).Should().BeTrue();
        DoctorConsoleNavigation.ShowDoctorsDirectory(state).Should().BeFalse();
        DoctorConsoleNavigation.ShowOperations(state).Should().BeFalse();
        DoctorConsoleNavigation.ShowClinicsDirectory(state).Should().BeFalse();
    }

    [Fact]
    public async Task Clinic_Admin_Is_Not_Doctor_Console_Actor()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.ClinicDashboardRead,
                WebPermissions.PatientsSearch,
                WebPermissions.AppointmentsRead,
            ],
            HasActiveStaffMembership = true,
        });

        state.IsDoctor.Should().BeFalse();
        DoctorConsoleNavigation.IsDoctorConsoleActor(state).Should().BeFalse();
        DoctorConsoleNavigation.ShowPatientsLink(state).Should().BeTrue();
    }

    [Fact]
    public void Dashboard_Page_Branches_To_Doctor_Dashboard_After_Clinic()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "HealthCare.Web",
            "Components",
            "Pages",
            "Dashboard.razor");
        var source = File.ReadAllText(path);
        source.Should().Contain("DoctorDashboardRead");
        source.Should().Contain("<DoctorDashboardView");
        var orgIdx = source.IndexOf("OrganizationDashboardRead", StringComparison.Ordinal);
        var clinicIdx = source.IndexOf("ClinicDashboardRead", StringComparison.Ordinal);
        var doctorIdx = source.IndexOf("DoctorDashboardRead", StringComparison.Ordinal);
        orgIdx.Should().BeLessThan(clinicIdx);
        clinicIdx.Should().BeLessThan(doctorIdx);
    }

    [Fact]
    public void StaffLayout_Shows_Patients_And_Hides_Doctor_Admin_Nav()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "HealthCare.Web",
            "Components",
            "Layout",
            "StaffLayout.razor");
        var source = File.ReadAllText(path);
        source.Should().Contain("DoctorConsoleNavigation.ShowPatientsLink");
        source.Should().Contain("DoctorConsoleNavigation.ShowOperations");
        source.Should().Contain("DoctorConsoleNavigation.ShowClinicsDirectory");
        source.Should().Contain("DoctorConsoleNavigation.ShowMyProfileLink");
        source.Should().Contain("/doctor/profile");
        source.Should().Contain("/patients");
    }

    [Fact]
    public void Doctor_Dashboard_View_Has_Safe_Cards_And_No_Medical_Notes()
    {
        var path = Path.Combine(
            FindRepoRoot(),
            "src",
            "HealthCare.Web",
            "Components",
            "Dashboard",
            "DoctorDashboardView.razor");
        var source = File.ReadAllText(path);
        source.Should().Contain("Doctor Dashboard");
        source.Should().Contain("Today’s appointments");
        source.Should().Contain("Awaiting completion");
        source.Should().Contain("Availability warnings");
        source.Should().Contain("/appointments");
        source.Should().Contain("/availability");
        source.Should().NotContain("/patients");
        source.Should().NotContain("/doctor/profile");
        source.ToLowerInvariant().Should().NotContain("medical note");
        source.Should().NotContain("Active staff");
        source.Should().NotContain("MaxClinics");
    }

    [Fact]
    public void Doctor_Dashboard_Api_Client_Exists()
    {
        typeof(IDoctorDashboardApiClient).Should().NotBeNull();
        typeof(DoctorDashboardApiClient).Should().NotBeNull();
        typeof(DoctorDashboardResponse).Should().NotBeNull();
    }

    [Fact]
    public void Problem_Messages_Map_Known_Codes()
    {
        var ex = new ApiProblemException(403, "Forbidden", "detail", DoctorDashboardErrorCodes.AccessDenied);
        DoctorDashboardProblemMessages.ToUserMessage(ex)
            .Should().Contain("permission");
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
