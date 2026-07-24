using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Staff;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;
using HealthCare.Web.Staff;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminStaffUiTests
{
    [Fact]
    public async Task Clinic_Admin_Sees_Staff_Page_Without_Clinic_Picker()
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
                WebPermissions.StaffManage,
                WebPermissions.StaffPasswordReset,
                WebPermissions.RolesRead,
                WebPermissions.RolesAssign,
                WebPermissions.SecuritySessionsRevoke,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        StaffPermissionRules.CanView(state).Should().BeTrue();
        StaffPermissionRules.CanManage(state).Should().BeTrue();
        StaffPermissionRules.ShowClinicPicker(state).Should().BeFalse();
        StaffPermissionRules.CanChangeClinic(state).Should().BeFalse();
        state.IsClinicAdmin.Should().BeTrue();
        state.CanFilterByClinic.Should().BeFalse();
        state.Has(WebPermissions.OrganizationProfileRead).Should().BeFalse();
        state.Has(WebPermissions.OrganizationUsageRead).Should().BeFalse();
        state.Has(WebPermissions.SecuritySessionsRead).Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Keeps_Clinic_Picker_And_Broader_Role_Filter()
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
                WebPermissions.StaffManage,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        StaffPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        StaffPermissionRules.CanChangeClinic(state).Should().BeTrue();
        StaffRoleFilterOptions.For(state).Should().Contain("ORGANIZATION_ADMIN");
        StaffRoleFilterOptions.For(state).Should().NotContain("PLATFORM_ADMIN");
        StaffPageCopy.Subtitle(state).Should().Contain("organization");
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
                WebPermissions.StaffManage,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = false,
        });

        StaffPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        StaffRoleFilterOptions.For(state).Should().Contain("PLATFORM_ADMIN");
        StaffPageCopy.Subtitle(state).Should().Contain("selected");
    }

    [Fact]
    public async Task Clinic_Admin_Role_Filter_Omits_Org_And_Platform_Roles()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions = [WebPermissions.StaffRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        var roles = StaffRoleFilterOptions.For(state);
        roles.Should().BeEquivalentTo(StaffRoleFilterOptions.ClinicAdmin);
        roles.Should().NotContain("ORGANIZATION_ADMIN");
        roles.Should().NotContain("PLATFORM_ADMIN");
        roles.Should().NotContain("PATIENT");
        StaffPageCopy.Subtitle(state).Should().Contain("your clinic");
    }

    [Fact]
    public void Staff_Page_Is_Actor_Aware_For_Clinic_Admin()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var staff = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Staff.razor"));
        var create = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "CreateStaffDialog.razor"));
        var roles = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "StaffRolesDialog.razor"));
        var activation = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "StaffActivationDialog.razor"));
        var password = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "StaffPasswordResetDialog.razor"));
        var revoke = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "StaffRevokeSessionsDialog.razor"));
        var edit = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "EditStaffDialog.razor"));
        var presentation = File.ReadAllText(Path.Combine(webRoot, "Staff", "StaffPresentation.cs"));

        staff.Should().Contain("IStaffManagementApiClient");
        staff.Should().Contain("StaffPermissionRules.ShowClinicPicker");
        staff.Should().Contain("StaffRoleFilterOptions.For");
        staff.Should().Contain("StaffPageCopy.Subtitle");
        staff.Should().Contain("CanDeactivate");
        staff.Should().Contain("StaffPermissionRules.CanChangeClinic");
        staff.Should().Contain("EmptyState");
        staff.Should().Contain("PageLoading");
        staff.Should().Contain("StaffProblemMessages");
        staff.Should().Contain("_page");
        staff.Should().Contain("staff-status");
        staff.Should().NotContain("CanFilterByClinic || Permissions.Has(WebPermissions.ClinicsRead)");
        staff.Should().NotContain("@inject HttpClient");
        staff.Should().NotContain("/organization/settings");
        staff.Should().NotContain("/usage");
        staff.Should().NotContain("/security");
        staff.Should().NotContain("MaxClinics");
        staff.Should().NotContain("billing");

        create.Should().Contain("AssignableByCurrentUser");
        create.Should().Contain("IsClinicAdmin");
        create.Should().Contain("membership clinic");
        create.Should().NotContain("ClinicPicker Label=\"Clinic\" AllowClear=\"false\" Disabled=\"true\"");

        roles.Should().Contain("AssignableByCurrentUser");
        roles.Should().Contain("IsOrganizationAdmin || Permissions.IsPlatformAdmin");
        activation.Should().Contain("ExpectedVersion");
        password.Should().Contain("IStaffManagementApiClient");
        revoke.Should().Contain("IStaffManagementApiClient");
        edit.Should().Contain("ExpectedVersion");
        edit.Should().Contain("StaffErrorCodes.ConcurrencyConflict");

        presentation.Should().Contain("ClinicAdmin");
        presentation.Should().NotContain("ORGANIZATION_ADMIN\", \"PLATFORM_ADMIN");
    }

    [Fact]
    public void Problem_Messages_Cover_Self_Last_Admin_And_Conflict()
    {
        StaffProblemMessages.From(
                new ApiProblemException(409, "Conflict", "raw", StaffErrorCodes.ConcurrencyConflict))
            .Should().Contain("Reload")
            .And.NotContain("raw");

        StaffProblemMessages.From(
                new ApiProblemException(403, "Self", "raw", StaffErrorCodes.SelfDeactivationDenied))
            .Should().Contain("own")
            .And.NotContain("raw");

        StaffProblemMessages.From(
                new ApiProblemException(409, "Protected", null, StaffErrorCodes.LastAdminProtected))
            .Should().Contain("protected");

        StaffProblemMessages.From(
                new ApiProblemException(401, "Auth", "raw-exception-stack", null))
            .Should().Contain("Sign in")
            .And.NotContain("raw-exception-stack");

        StaffProblemMessages.From(
                new ApiProblemException(403, "Denied", "raw-exception-stack", null))
            .Should().Contain("permission")
            .And.NotContain("raw-exception-stack");

        StaffProblemMessages.From(
                new ApiProblemException(403, "Denied", null, StaffErrorCodes.RoleAssignmentDenied))
            .Should().Contain("not permitted");

        StaffProblemMessages.From(
                new ApiProblemException(404, "Missing", null, StaffErrorCodes.NotFound))
            .Should().Contain("not found");
    }

    [Fact]
    public void Dialogs_Use_Typed_Client_Confirmations()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var password = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "StaffPasswordResetDialog.razor"));
        var revoke = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "StaffRevokeSessionsDialog.razor"));
        var create = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "CreateStaffDialog.razor"));

        password.Should().Contain("RequestPasswordResetAsync");
        revoke.Should().Contain("RevokeSessionsAsync");
        create.Should().Contain("CreateAsync");
        password.Should().NotContain("resetToken");
        revoke.Should().NotContain("refreshToken");
    }
}
