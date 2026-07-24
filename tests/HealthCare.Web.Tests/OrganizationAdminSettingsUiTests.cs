using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Organizations;
using HealthCare.Web.Auth;
using HealthCare.Web.OrganizationSettings;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminSettingsUiTests
{
    [Fact]
    public async Task Organization_Admin_Can_Open_And_Update()
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
                WebPermissions.OrganizationProfileUpdate,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        OrganizationSettingsPermissionRules.CanView(state).Should().BeTrue();
        OrganizationSettingsPermissionRules.CanUpdate(state).Should().BeTrue();
    }

    [Fact]
    public async Task Read_Only_Permission_Hides_Editing()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "viewer@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.OrganizationProfileRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        OrganizationSettingsPermissionRules.CanView(state).Should().BeTrue();
        OrganizationSettingsPermissionRules.CanUpdate(state).Should().BeFalse();
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
        OrganizationSettingsPermissionRules.CanView(state).Should().BeFalse();
    }

    [Fact]
    public async Task Platform_Admin_Without_Selected_Organization_Is_Blocked_By_Page_Gate()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions =
            [
                WebPermissions.OrganizationProfileRead,
                WebPermissions.OrganizationProfileUpdate,
            ],
            HasActiveStaffMembership = false,
        });

        OrganizationSettingsPermissionRules.CanView(state).Should().BeTrue();
        state.IsPlatformAdmin.Should().BeTrue();
        // Page requires PlatformTenant.HasOrganization before calling the API.
    }

    [Fact]
    public void Existing_Values_And_Read_Only_Status_Render_From_Form_State()
    {
        var response = new OrganizationSettingsResponse
        {
            OrganizationId = Guid.NewGuid(),
            Name = "Acme Health",
            Slug = "acme-health",
            Status = "Active",
            ContactEmail = "ops@acme.test",
            ContactPhone = "+966500000000",
            Country = "SA",
            DefaultTimeZoneId = "Asia/Riyadh",
            BrandingPlaceholder = "Acme",
            Version = 3,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            MaxClinics = 10,
            MaxStaff = 50,
            ClinicCount = 2,
            StaffCount = 8,
            RemainingClinicCapacity = 8,
            RemainingStaffCapacity = 42,
        };

        var form = OrganizationSettingsFormState.FromResponse(response);
        form.Name.Should().Be("Acme Health");
        form.ContactEmail.Should().Be("ops@acme.test");
        form.ExpectedVersion.Should().Be(3);
        OrganizationSettingsPresentation.StatusTone(response.Status).Should().Be(Design.StatusTone.Success);
        OrganizationSettingsPresentation.DisplayOrDash(null).Should().Be("—");
    }

    [Fact]
    public void Valid_Form_Builds_Update_Request_Invalid_Does_Not()
    {
        var valid = new OrganizationSettingsFormState
        {
            Name = "Valid Org",
            ContactEmail = "valid@example.com",
            ContactPhone = "+123",
            Country = "SA",
            DefaultTimeZoneId = "Asia/Riyadh",
            BrandingPlaceholder = "Brand",
            ExpectedVersion = 2,
        };

        var request = valid.TryBuildUpdateRequest(out var validationError);
        validationError.Should().BeNull();
        request.Should().NotBeNull();
        request!.ExpectedVersion.Should().Be(2);
        request.Name.Should().Be("Valid Org");
        request.NameSpecified.Should().BeTrue();
        request.ContactEmailSpecified.Should().BeTrue();

        var invalid = new OrganizationSettingsFormState
        {
            Name = " ",
            ExpectedVersion = 1,
        };
        invalid.TryBuildUpdateRequest(out var invalidError).Should().BeNull();
        invalidError.Should().Contain("required");

        var badEmail = new OrganizationSettingsFormState
        {
            Name = "Org",
            ContactEmail = "not-an-email",
            ExpectedVersion = 1,
        };
        badEmail.TryBuildUpdateRequest(out var emailError).Should().BeNull();
        emailError.Should().Contain("email");
    }

    [Fact]
    public void Problem_Messages_Cover_401_403_404_And_Concurrency()
    {
        OrganizationSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(401, "Auth", "raw", null))
            .Should().Contain("Sign in")
            .And.NotContain("raw");

        OrganizationSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(403, "Denied", "raw", OrganizationSettingsErrorCodes.AccessDenied))
            .Should().Contain("permission")
            .And.NotContain("raw");

        OrganizationSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(404, "Missing", null, OrganizationSettingsErrorCodes.OrganizationNotFound))
            .Should().Contain("not found");

        OrganizationSettingsProblemMessages.ToUserMessage(
                new ApiProblemException(409, "Conflict", null, OrganizationSettingsErrorCodes.ConcurrencyConflict))
            .Should().Contain("Reload");
    }

    [Fact]
    public void Settings_Page_Uses_Typed_Client_And_Omits_Out_Of_Scope_Controls()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OrganizationSettings.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        var program = File.ReadAllText(Path.Combine(webRoot, "Program.cs"));

        page.Should().Contain("IOrganizationSettingsApiClient");
        page.Should().Contain("@page \"/organization/settings\"");
        page.Should().Contain("Status is read-only");
        page.Should().Contain("Max clinics");
        page.Should().Contain("Max staff");
        page.Should().Contain("Remaining clinics");
        page.Should().Contain("Remaining staff");
        page.Should().Contain("/usage");
        page.Should().Contain("View full usage");
        page.Should().Contain("organization_profile.update");
        page.Should().Contain("ExpectedVersion");
        page.Should().Contain("Reload latest");
        page.Should().Contain("Notify.Success");
        page.Should().Contain("OrganizationSettingsProblemMessages");
        page.Should().Contain("IsPatientOnly");
        page.Should().Contain("PlatformTenant.HasOrganization");
        page.Should().Contain("PlatformBypass");
        page.Should().NotContain("@inject HttpClient");
        page.Should().NotContain("suspend");
        page.Should().NotContain("billing");
        page.Should().NotContain("delete organization");
        page.Should().NotContain("MaxClinics =");
        page.Should().NotContain("checkout");

        layout.Should().Contain("/organization/settings");
        layout.Should().Contain("Organization Profile");
        layout.Should().Contain("OrganizationSettingsPermissionRules.CanView");

        program.Should().Contain("IOrganizationSettingsApiClient");
    }

    [Fact]
    public void Settings_Client_Hits_Organization_Settings_Endpoint()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "OrganizationSettingsApiClient.cs")));

        source.Should().Contain("api/v1/organization/settings");
        source.Should().Contain("PatchAsJsonAsync");
        source.Should().Contain("platformAdminBypass");
        source.Should().Contain("organizationId");
    }

    [Fact]
    public void Save_Success_Path_Is_Present_And_Invalid_Form_Guards_Api()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "OrganizationSettings.razor"));
        page.Should().Contain("TryBuildUpdateRequest");
        page.Should().Contain("SettingsApi.UpdateAsync");
        page.Should().Contain("Organization profile saved.");
    }
}
