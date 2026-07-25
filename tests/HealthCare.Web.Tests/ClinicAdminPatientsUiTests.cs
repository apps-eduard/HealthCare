using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Patients;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminPatientsUiTests
{
    [Fact]
    public async Task Clinic_Admin_Sees_Patients_Without_Clinic_Picker_Or_Cross_Clinic_Enroll()
    {
        var state = await ClinicAdminStateAsync();

        PatientDirectoryPermissionRules.CanView(state).Should().BeTrue();
        PatientDirectoryPermissionRules.ShowClinicPicker(state).Should().BeFalse();
        PatientDirectoryPermissionRules.CanUpdateClinicStatus(state).Should().BeTrue();
        PatientDirectoryPermissionRules.CanEnrollAcrossClinics(state).Should().BeFalse();
        PatientDirectoryPageCopy.Subtitle(state).Should().Contain("your clinic");
        state.Has("medical_notes.read").Should().BeFalse();
        state.Has(WebPermissions.OrganizationProfileRead).Should().BeFalse();
        state.Has(WebPermissions.OrganizationUsageRead).Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Keeps_Clinic_Picker_And_Cross_Clinic_Enroll()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.PatientsSearch,
                WebPermissions.PatientsRead,
                WebPermissions.PatientsUpdateClinicStatus,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        PatientDirectoryPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        PatientDirectoryPermissionRules.CanEnrollAcrossClinics(state).Should().BeTrue();
        PatientDirectoryPageCopy.Subtitle(state).Should().Contain("Organization");
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
                WebPermissions.PatientsSearch,
                WebPermissions.PatientsRead,
                WebPermissions.PatientsUpdateClinicStatus,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = false,
        });

        PatientDirectoryPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        PatientDirectoryPermissionRules.CanEnrollAcrossClinics(state).Should().BeTrue();
        PatientDirectoryPageCopy.Subtitle(state).Should().Contain("selected");
    }

    [Fact]
    public void Patients_Page_Is_Actor_Aware_For_Clinic_Admin()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Patients.razor"));
        var drawer = File.ReadAllText(Path.Combine(webRoot, "Components", "Patients", "PatientDetailDrawer.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));

        page.Should().Contain("PatientDirectoryPageCopy.Subtitle");
        page.Should().Contain("PatientDirectoryPermissionRules.ShowClinicPicker");
        page.Should().Contain("patients-clinic-caption");
        page.Should().Contain("CanEnrollAcrossClinics");
        page.Should().Contain("aria-label=\"Clinic patients\"");
        page.Should().NotContain("ClinicPicker Label=\"Clinic\" AllowClear=\"false\" Required=\"false\" Disabled=\"true\"");
        page.Should().NotContain("@inject HttpClient");
        page.Should().NotContain("medical_notes");
        page.Should().NotContain("MedicalNote");

        drawer.Should().Contain("PatientDirectoryPermissionRules.CanEnrollAcrossClinics");
        drawer.Should().Contain("ExpectedVersion");
        drawer.Should().Contain("Update enrollment status");
        drawer.Should().NotContain("MedicalNote");

        layout.Should().Contain("RouterLink=\"/patients\"");
        layout.Should().Contain("DoctorConsoleNavigation.ShowPatientsLink");
    }

    [Fact]
    public void Safe_Error_And_Concurrency_Messages()
    {
        PatientProblemMessages.ToUserMessage(new ApiProblemException(400, "Bad", "raw", null))
            .Should().NotContain("raw");
        PatientProblemMessages.ToUserMessage(new ApiProblemException(401, "Unauthorized", "x", null))
            .Should().Contain("Sign in");
        PatientProblemMessages.ToUserMessage(new ApiProblemException(403, "Forbidden", "x", null))
            .Should().Contain("permission");
        PatientProblemMessages.ToUserMessage(new ApiProblemException(404, "Missing", "x", null))
            .Should().Contain("not found");
        PatientProblemMessages.IsConcurrencyConflict(new ApiProblemException(
            409, "Conflict", null, PatientErrorCodes.ClinicPatientConcurrencyConflict)).Should().BeTrue();
        PatientProblemMessages.ToUserMessage(new ApiProblemException(
            409, "Conflict", null, PatientErrorCodes.ClinicPatientConcurrencyConflict))
            .Should().Contain("Reload");
    }

    [Fact]
    public void Status_Options_Are_Backend_Approved_Values()
    {
        PatientStatusPresentation.ClinicPatientStatuses.Should().Equal("Active", "Inactive");
        PatientStatusPresentation.ClinicPatientLabel("Active").Should().Be("Active");
        PatientStatusPresentation.ClinicPatientTone("Inactive").Should().Be(StatusTone.Default);
    }

    [Fact]
    public void Patients_Page_Supports_Search_Filters_Pagination_And_Safe_Fields()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Patients.razor"));
        var drawer = File.ReadAllText(Path.Combine(webRoot, "Components", "Patients", "PatientDetailDrawer.razor"));

        page.Should().Contain("patients-search");
        page.Should().Contain("patients-enrollment");
        page.Should().Contain("patients-active");
        page.Should().Contain("_page");
        page.Should().Contain("_pageSize");
        page.Should().Contain("LocalPatientNumber");
        page.Should().Contain("ClinicPatientStatus");
        page.Should().Contain("EmptyState");
        page.Should().Contain("_loading");
        page.Should().Contain("PatientProblemMessages");
        page.Should().NotContain("Organization Profile");
        page.Should().NotContain("Usage & Limits");
        page.Should().NotContain("medical_notes");

        drawer.Should().Contain("WebPermissions.PatientsUpdateClinicStatus");
        drawer.Should().Contain("UpdateClinicProfileAsync");
        drawer.Should().Contain("ExpectedVersion");
        drawer.Should().Contain("IsConcurrencyConflict");
    }

    [Fact]
    public async Task Enrollment_Without_Permission_Is_Blocked_By_Rules()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doc@test.local",
            Roles = ["DOCTOR"],
            Permissions = [WebPermissions.PatientsSearch, WebPermissions.PatientsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        PatientDirectoryPermissionRules.CanUpdateClinicStatus(state).Should().BeFalse();
        PatientDirectoryPermissionRules.CanEnrollAcrossClinics(state).Should().BeFalse();
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
                WebPermissions.PatientsSearch,
                WebPermissions.PatientsRead,
                WebPermissions.PatientsUpdateClinicStatus,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        return state;
    }
}
