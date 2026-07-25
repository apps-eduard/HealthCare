using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.ClinicSettings;
using HealthCare.Web.OrganizationSettings;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminSettingsUiTests
{
    [Fact]
    public async Task Clinic_Admin_Can_Open_Clinic_Settings()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "clinicadmin@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.ClinicProfileRead,
                WebPermissions.ClinicProfileUpdate,
                WebPermissions.ClinicDashboardRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        ClinicSettingsPermissionRules.CanView(state).Should().BeTrue();
        ClinicSettingsPermissionRules.CanUpdate(state).Should().BeTrue();
        state.Has(WebPermissions.OrganizationProfileRead).Should().BeFalse();
    }

    [Fact]
    public async Task Read_Only_User_Cannot_Edit()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "viewer@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions = [WebPermissions.ClinicProfileRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        ClinicSettingsPermissionRules.CanView(state).Should().BeTrue();
        ClinicSettingsPermissionRules.CanUpdate(state).Should().BeFalse();
    }

    [Fact]
    public async Task Patient_Is_Blocked()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "patient@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [],
            HasActiveStaffMembership = false,
        });

        state.IsPatientOnly.Should().BeTrue();
        ClinicSettingsPermissionRules.CanView(state).Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Does_Not_See_Clinic_Profile_Permission()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.OrganizationProfileRead,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        ClinicSettingsPermissionRules.CanView(state).Should().BeFalse();
        OrganizationSettingsPermissionRules.CanView(state).Should().BeTrue();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Clinic_Context()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions =
            [
                WebPermissions.ClinicProfileRead,
                WebPermissions.ClinicProfileUpdate,
            ],
            HasActiveStaffMembership = false,
        });

        ClinicSettingsPermissionRules.CanView(state).Should().BeTrue();
        state.IsPlatformAdmin.Should().BeTrue();
        // Page requires PlatformTenant.HasClinic + ExplicitBypassEnabled before calling the API.
    }

    [Fact]
    public void Existing_Values_And_Read_Only_Fields_Render_From_Form_State()
    {
        var response = new ClinicSettingsResponse
        {
            ClinicId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            OrganizationName = "Acme Org",
            Name = "Acme Clinic",
            Slug = "acme-clinic",
            Specialty = "General",
            ContactEmail = "ops@acme.test",
            ContactPhone = "+966500000000",
            Address = "Main St",
            City = "Riyadh",
            Country = "SA",
            DefaultTimeZoneId = "Asia/Riyadh",
            IsActive = true,
            Version = 3,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var form = ClinicSettingsFormState.FromResponse(response);
        form.Name.Should().Be("Acme Clinic");
        form.ContactEmail.Should().Be("ops@acme.test");
        form.ExpectedVersion.Should().Be(3);
        ClinicSettingsPresentation.StatusText(response.IsActive).Should().Be("Active");
        ClinicSettingsPresentation.StatusTone(response.IsActive).Should().Be(Design.StatusTone.Success);
        ClinicSettingsPresentation.DisplayOrDash(null).Should().Be("—");
    }

    [Fact]
    public void Valid_Save_Builds_Request_Invalid_Does_Not()
    {
        var valid = new ClinicSettingsFormState
        {
            Name = "Valid Clinic",
            Specialty = "Cardio",
            ContactEmail = "valid@example.com",
            ContactPhone = "+123",
            Address = "Street",
            City = "Riyadh",
            Country = "SA",
            DefaultTimeZoneId = "Asia/Riyadh",
            ExpectedVersion = 2,
        };

        var request = valid.TryBuildUpdateRequest(out var validationError);
        validationError.Should().BeNull();
        request.Should().NotBeNull();
        request!.ExpectedVersion.Should().Be(2);
        request.Name.Should().Be("Valid Clinic");
        request.NameSpecified.Should().BeTrue();
        request.ContactEmailSpecified.Should().BeTrue();

        var invalid = new ClinicSettingsFormState
        {
            Name = " ",
            DefaultTimeZoneId = "Asia/Riyadh",
            ExpectedVersion = 1,
        };
        invalid.TryBuildUpdateRequest(out var invalidError).Should().BeNull();
        invalidError.Should().Contain("required");

        var badEmail = new ClinicSettingsFormState
        {
            Name = "Clinic",
            ContactEmail = "not-an-email",
            DefaultTimeZoneId = "Asia/Riyadh",
            ExpectedVersion = 1,
        };
        badEmail.TryBuildUpdateRequest(out var emailError).Should().BeNull();
        emailError.Should().Contain("email");
    }

    [Fact]
    public void Problem_Messages_Cover_401_403_404_400_And_Concurrency()
    {
        ClinicSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(401, "Auth", "raw", null))
            .Should().Contain("Sign in")
            .And.NotContain("raw");

        ClinicSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(403, "Denied", "raw", ClinicSettingsErrorCodes.AccessDenied))
            .Should().Contain("permission")
            .And.NotContain("raw");

        ClinicSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(404, "Missing", null, ClinicSettingsErrorCodes.ClinicNotFound))
            .Should().Contain("not found");

        ClinicSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(400, "Bad", null, ClinicSettingsErrorCodes.EmptyUpdate))
            .Should().Contain("at least one");

        ClinicSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(409, "Conflict", null, ClinicSettingsErrorCodes.ConcurrencyConflict))
            .Should().Contain("Reload");
    }

    [Fact]
    public void Settings_Page_Uses_Typed_Client_And_Omits_Out_Of_Scope_Controls()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "ClinicSettings.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        var program = File.ReadAllText(Path.Combine(webRoot, "Program.cs"));

        page.Should().Contain("IClinicSettingsApiClient");
        page.Should().Contain("@page \"/clinic/settings\"");
        page.Should().Contain("Clinic Profile");
        page.Should().Contain("Status is read-only");
        page.Should().Contain("Slug:");
        page.Should().Contain("Organization:");
        page.Should().Contain("clinic_profile.update");
        page.Should().Contain("ExpectedVersion");
        page.Should().Contain("Reload latest");
        page.Should().Contain("Notify.Success");
        page.Should().Contain("ClinicSettingsProblemMessages");
        page.Should().Contain("IsPatientOnly");
        page.Should().Contain("PlatformTenant.HasClinic");
        page.Should().Contain("PlatformBypass");
        page.Should().Contain("TryBuildUpdateRequest");
        page.Should().Contain("SettingsApi.UpdateAsync");
        page.Should().Contain("Clinic profile saved.");
        page.Should().NotContain("@inject HttpClient");
        page.Should().NotContain("MaxClinics");
        page.Should().NotContain("MaxStaff");
        page.Should().NotContain("billing");
        page.Should().NotContain("subscription");
        page.Should().NotContain("deactivate");
        page.Should().NotContain("delete clinic");
        page.Should().NotContain("activate");
        page.Should().NotContain("Exception.Message");
        page.Should().NotContain("StackTrace");

        layout.Should().Contain("/clinic/settings");
        layout.Should().Contain("Clinic Profile");
        layout.Should().Contain("ClinicSettingsPermissionRules.CanView");
        layout.Should().Contain("clinic-settings");
        layout.Should().Contain("/clinic/reports");
        layout.Should().NotContain("/clinic/audit");

        program.Should().Contain("IClinicSettingsApiClient");
    }

    [Fact]
    public void Settings_Client_Hits_Clinic_Settings_Endpoint_With_WhenWritingNull()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "ClinicSettingsApiClient.cs")));

        source.Should().Contain("api/v1/clinic/settings");
        source.Should().Contain("PatchAsJsonAsync");
        source.Should().Contain("WhenWritingNull");
        source.Should().Contain("platformAdminBypass");
        source.Should().Contain("clinicId");
    }

    [Fact]
    public void Navigation_Highlights_Clinic_Settings()
    {
        var layout = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Components", "Layout", "StaffLayout.razor")));

        layout.Should().Contain("/clinic/settings");
        layout.Should().Contain("clinic-settings");
        layout.Should().Contain("StartsWith(\"/clinic/settings\"");
    }

    [Fact]
    public void WebPermissions_Exposes_Clinic_Profile_Constants()
    {
        WebPermissions.ClinicProfileRead.Should().Be("clinic_profile.read");
        WebPermissions.ClinicProfileUpdate.Should().Be("clinic_profile.update");
        typeof(IClinicSettingsApiClient).GetMethod(nameof(IClinicSettingsApiClient.GetAsync))
            .Should().NotBeNull();
        typeof(IClinicSettingsApiClient).GetMethod(nameof(IClinicSettingsApiClient.UpdateAsync))
            .Should().NotBeNull();
    }
}
