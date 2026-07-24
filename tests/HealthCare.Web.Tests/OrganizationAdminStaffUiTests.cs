using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Staff;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;
using HealthCare.Web.Staff;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminStaffUiTests
{
    [Fact]
    public void Staff_Problem_Messages_Are_Safe_For_Known_Codes()
    {
        var conflict = new ApiProblemException(409, "Conflict", null, StaffErrorCodes.ConcurrencyConflict);
        StaffProblemMessages.From(conflict).Should().Contain("Reload");

        var self = new ApiProblemException(403, "Self", null, StaffErrorCodes.SelfDeactivationDenied);
        StaffProblemMessages.From(self).Should().Contain("own");

        var limit = new ApiProblemException(409, "Limit", null, StaffErrorCodes.LimitReached);
        StaffProblemMessages.From(limit).Should().Contain("limit");

        var lastAdmin = new ApiProblemException(409, "Protected", null, StaffErrorCodes.LastAdminProtected);
        StaffProblemMessages.From(lastAdmin).Should().Contain("protected");
    }

    [Fact]
    public async Task Staff_Read_Is_Enough_For_Directory_Gate()
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
                WebPermissions.RolesRead,
                WebPermissions.RolesAssign,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.StaffRead).Should().BeTrue();
        state.Has(WebPermissions.StaffManage).Should().BeTrue();
        state.Has(WebPermissions.RolesAssign).Should().BeTrue();
        state.IsOrganizationAdmin.Should().BeTrue();
        state.IsPatientOnly.Should().BeFalse();
    }

    [Fact]
    public void Staff_Page_Uses_Typed_Client_Tabs_And_Clinic_Context()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var staff = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Staff.razor"));
        var roles = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "StaffRolesDialog.razor"));
        var edit = File.ReadAllText(Path.Combine(webRoot, "Components", "Staff", "EditStaffDialog.razor"));

        staff.Should().Contain("IStaffManagementApiClient");
        staff.Should().Contain("IClinicWorkingContext");
        staff.Should().Contain("SearchClinicAdminsAsync");
        staff.Should().Contain("StaffProblemMessages");
        staff.Should().Contain("@page \"/staff\"");
        staff.Should().Contain("@page \"/staff/clinic-admins\"");
        staff.Should().Contain("@page \"/staff/doctors\"");
        staff.Should().Contain("@page \"/staff/nurses\"");
        staff.Should().Contain("@page \"/staff/receptionists\"");
        staff.Should().Contain("WebPermissions.StaffRead");
        staff.Should().Contain("CanDeactivate");
        staff.Should().NotContain("@inject HttpClient");

        roles.Should().Contain("AssignRoleAsync");
        roles.Should().Contain("StaffProblemMessages");
        roles.Should().Contain("replaces the current staff role");

        edit.Should().Contain("ExpectedVersion");
        edit.Should().Contain("StaffErrorCodes.ConcurrencyConflict");
    }

    [Fact]
    public void Staff_Management_Client_Includes_Clinic_Admins_And_Role_Endpoints()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "StaffManagementApiClient.cs")));

        source.Should().Contain("api/v1/staff-management/staff");
        source.Should().Contain("api/v1/staff-management/clinic-admins");
        source.Should().Contain("SearchClinicAdminsAsync");
        source.Should().Contain("AssignRoleAsync");
        source.Should().Contain("RemoveRoleAsync");
        source.Should().Contain("change-clinic");
        source.Should().Contain("password-reset");
        source.Should().Contain("revoke-sessions");
        source.Should().Contain("search=");
        source.Should().Contain("role=");
        source.Should().Contain("isActive=");
        source.Should().Contain("clinicId=");
        source.Should().Contain("page=");
        source.Should().Contain("pageSize=");
    }

    [Fact]
    public void Web_Permissions_Include_Staff_Lifecycle_Constants()
    {
        typeof(WebPermissions).GetField(nameof(WebPermissions.StaffRead)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.StaffManage)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.StaffPasswordReset)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.RolesRead)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.RolesAssign)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.SecuritySessionsRevoke)).Should().NotBeNull();
    }
}
