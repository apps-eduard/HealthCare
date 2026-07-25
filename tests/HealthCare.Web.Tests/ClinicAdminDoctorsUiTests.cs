using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Staff;
using HealthCare.Web.Auth;
using HealthCare.Web.Availability;
using HealthCare.Web.Doctors;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminDoctorsUiTests
{
    [Fact]
    public async Task Clinic_Admin_Sees_Doctors_Navigation_And_Page_Rules()
    {
        var state = await ClinicAdminStateAsync();

        DoctorDirectoryPermissionRules.CanView(state).Should().BeTrue();
        DoctorDirectoryPermissionRules.ShowClinicPicker(state).Should().BeFalse();
        DoctorDirectoryPermissionRules.UseStaffDirectory(state).Should().BeTrue();
        DoctorDirectoryPermissionRules.CanOpenAvailability(state).Should().BeTrue();
        DoctorDirectoryPermissionRules.CanOpenAppointments(state).Should().BeTrue();
        DoctorDirectoryPageCopy.Subtitle(state).Should().Contain("your clinic");
        state.Has("medical_notes.read").Should().BeFalse();
    }

    [Fact]
    public async Task Clinic_Picker_Absent_For_Clinic_Admin_On_Doctors_And_Availability()
    {
        var state = await ClinicAdminStateAsync();
        DoctorDirectoryPermissionRules.ShowClinicPicker(state).Should().BeFalse();
        state.CanFilterByClinic.Should().BeFalse();
        AvailabilityPermissionRules.CanManage(state).Should().BeTrue();
    }

    [Fact]
    public void Doctors_Page_And_Layout_Wire_Clinic_Admin_Directory()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Doctors.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        var availability = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Availability.razor"));
        var appointments = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Appointments.razor"));

        page.Should().Contain("@page \"/doctors\"");
        page.Should().Contain("DoctorDirectoryPermissionRules");
        page.Should().Contain("Role = \"DOCTOR\"");
        page.Should().Contain("IStaffManagementApiClient");
        page.Should().Contain("IDoctorAvailabilityApiClient");
        page.Should().Contain("ShowClinicPicker");
        page.Should().NotContain("@inject HttpClient");
        page.Should().NotContain("medical_notes");
        page.Should().Contain("AvailabilityHref");
        page.Should().Contain("AppointmentsHref");
        page.Should().Contain("PageLoading");
        page.Should().Contain("EmptyState");
        page.Should().Contain("ErrorState");

        layout.Should().Contain("RouterLink=\"/doctors\"");
        layout.Should().Contain("DoctorDirectoryPermissionRules.CanView");

        availability.Should().Contain("SupplyParameterFromQuery");
        availability.Should().Contain("doctorId");
        availability.Should().Contain("CanFilterByClinic");
        availability.Should().Contain("availability-clinic-caption");
        availability.Should().NotContain("ClinicPicker Label=\"Clinic\" AllowClear=\"false\" Required=\"false\" Disabled=\"true\"");

        appointments.Should().Contain("SupplyParameterFromQuery");
        appointments.Should().Contain("doctorId");
    }

    [Fact]
    public void Doctor_Directory_Links_And_Display_Helpers()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        DoctorDirectoryDisplay.AvailabilityHref(id).Should().Be($"/availability?doctorId={id:D}");
        DoctorDirectoryDisplay.AppointmentsHref(id).Should().Be($"/appointments?doctorId={id:D}");

        var name = DoctorDirectoryDisplay.Name(new StaffSummaryResponse
        {
            StaffMemberId = id,
            UserId = Guid.NewGuid(),
            Email = "d@test.local",
            FirstName = "Ava",
            LastName = "Adams",
            DisplayName = null,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            Role = "DOCTOR",
            MembershipIsActive = true,
            AccountIsActive = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Version = 1,
        });
        name.Should().Be("Ava Adams");
    }

    [Fact]
    public void Safe_Error_Messages_For_Doctor_Directory()
    {
        DoctorDirectoryProblemMessages.ToUserMessage(new ApiProblemException(400, "Bad", "raw", "validation"))
            .Should().NotContain("raw");
        DoctorDirectoryProblemMessages.ToUserMessage(new ApiProblemException(401, "Unauthorized", "x", "auth"))
            .Should().Contain("Sign in");
        DoctorDirectoryProblemMessages.ToUserMessage(new ApiProblemException(403, "Forbidden", "x", "authz"))
            .Should().Contain("permission");
        DoctorDirectoryProblemMessages.ToUserMessage(new ApiProblemException(404, "Not Found", "x", "missing"))
            .Should().Contain("not found");
        DoctorDirectoryProblemMessages.ToUserMessage(new ApiProblemException(409, "Conflict", "x", "conflict"))
            .Should().Contain("Reload");
    }

    [Fact]
    public void Availability_Client_Validation_Blocks_Invalid_Schedule()
    {
        AvailabilityPresentation.IsValidWindow("12:00", "11:00").Should().BeFalse();
        AvailabilityPresentation.IsValidWindow("09:00", "11:00").Should().BeTrue();
        AvailabilityPresentation.ExceptionRequiresTimes("UnavailableFullDay").Should().BeFalse();
        AvailabilityPresentation.ExceptionRequiresTimes("UnavailableRange").Should().BeTrue();
        AvailabilityProblemMessages.IsConcurrencyConflict(new ApiProblemException(
            409, "Conflict", "stale", AvailabilityErrorCodes.AvailabilityConcurrency)).Should().BeTrue();
    }

    [Fact]
    public async Task Organization_Admin_Keeps_Clinic_Picker_On_Doctor_Directory()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.StaffRead,
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageOrganization,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        DoctorDirectoryPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        DoctorDirectoryPageCopy.Subtitle(state).Should().Contain("Organization");
        state.Has(WebPermissions.ClinicDashboardRead).Should().BeFalse();
    }

    [Fact]
    public async Task Platform_Admin_Keeps_Explicit_Context_Behavior()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions =
            [
                WebPermissions.StaffRead,
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageOrganization,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = false,
        });

        DoctorDirectoryPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        DoctorDirectoryPageCopy.Subtitle(state).Should().Contain("selected");
    }

    [Fact]
    public void Medical_Note_Controls_Absent_From_Doctors_Page()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Doctors.razor"));
        page.Should().NotContain("MedicalNote");
        page.Should().NotContain("medical note");
        page.Should().Contain("Staff account");
    }

    private static async Task<PermissionState> ClinicAdminStateAsync()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "clinicadmin@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.StaffRead,
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageClinic,
                WebPermissions.AppointmentsRead,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        return state;
    }
}
