using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class PermissionAuthorizationMatrixTests
{
    [Fact]
    public void Every_Permission_Constant_Is_Unique()
    {
        Permissions.All.Should().OnlyHaveUniqueItems();
        Permissions.All.Should().NotBeEmpty();
    }

    [Fact]
    public void Every_Role_Mapping_Uses_Known_Permissions()
    {
        foreach (var role in AppRoles.All)
        {
            foreach (var permission in RolePermissionMatrix.GetPermissionsForRole(role))
            {
                Permissions.IsKnown(permission).Should().BeTrue($"role {role} maps unknown {permission}");
            }
        }
    }

    [Fact]
    public void Unknown_Role_Receives_No_Permissions()
    {
        RolePermissionMatrix.GetPermissionsForRole("NOT_A_ROLE").Should().BeEmpty();
    }

    [Fact]
    public void Unknown_Permission_Fails_Closed()
    {
        var sut = CreatePermissionService(
            authenticated: true,
            roles: [AppRoles.ClinicAdmin],
            staffActive: true,
            staffRole: AppRoles.ClinicAdmin);

        sut.HasPermission("not.a.real.permission").Should().BeFalse();
        var act = () => sut.RequirePermission("not.a.real.permission");
        act.Should().Throw<AuthorizationException>()
            .Which.ErrorCode.Should().Be(AuthorizationErrorCodes.InvalidPermission);
    }

    [Fact]
    public void Disabled_Or_Unauthenticated_User_Denied()
    {
        var sut = CreatePermissionService(authenticated: false, roles: [AppRoles.Doctor], staffActive: true, staffRole: AppRoles.Doctor);
        sut.HasPermission(Permissions.Appointments.Read).Should().BeFalse();
        sut.GetCurrentPermissions().Should().BeEmpty();
    }

    [Fact]
    public void Inactive_Membership_Strips_Staff_Permissions()
    {
        var sut = CreatePermissionService(
            authenticated: true,
            roles: [AppRoles.Receptionist],
            staffActive: false,
            staffRole: AppRoles.Receptionist);

        sut.HasPermission(Permissions.Patients.Search).Should().BeFalse();
        sut.HasPermission(Permissions.Appointments.Create).Should().BeFalse();
    }

    [Fact]
    public void Patient_May_Update_Own_Profile_And_Own_Appointments()
    {
        var sut = CreatePermissionService(
            authenticated: true,
            roles: [AppRoles.Patient],
            staffActive: false,
            staffRole: string.Empty,
            linkedPatient: true);

        sut.HasPermission(Permissions.Patients.UpdateOwnProfile).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Read).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Create).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Cancel).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Reschedule).Should().BeTrue();
        sut.HasPermission(Permissions.Patients.Search).Should().BeFalse();
        sut.HasPermission(Permissions.Reminders.Read).Should().BeFalse();
        sut.HasPermission(Permissions.Summaries.Read).Should().BeFalse();
        sut.HasPermission(Permissions.Availability.ManageSelf).Should().BeFalse();
    }

    [Fact]
    public void Patient_Without_Linkage_Loses_Patient_Permissions()
    {
        var sut = CreatePermissionService(
            authenticated: true,
            roles: [AppRoles.Patient],
            staffActive: false,
            staffRole: string.Empty,
            linkedPatient: false);

        sut.HasPermission(Permissions.Appointments.Create).Should().BeFalse();
    }

    [Fact]
    public void Receptionist_May_Operate_But_Cannot_Complete_Or_Manage_Availability()
    {
        var sut = CreateStaff(AppRoles.Receptionist);
        sut.HasPermission(Permissions.Appointments.Create).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Confirm).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.CheckIn).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Complete).Should().BeFalse();
        sut.HasPermission(Permissions.Appointments.NoShow).Should().BeFalse();
        sut.HasPermission(Permissions.Availability.ManageClinic).Should().BeFalse();
        sut.HasPermission(Permissions.Roles.Assign).Should().BeFalse();
    }

    [Fact]
    public void Doctor_May_Manage_Own_Availability_Only()
    {
        var sut = CreateStaff(AppRoles.Doctor);
        sut.HasPermission(Permissions.Availability.ManageSelf).Should().BeTrue();
        sut.HasPermission(Permissions.Availability.ManageClinic).Should().BeFalse();
        sut.HasPermission(Permissions.Appointments.Complete).Should().BeTrue();
    }

    [Fact]
    public void Clinic_Admin_Has_Clinic_Operations_And_Role_Assign()
    {
        var sut = CreateStaff(AppRoles.ClinicAdmin);
        sut.HasPermission(Permissions.Patients.Search).Should().BeTrue();
        sut.HasPermission(Permissions.Availability.ManageClinic).Should().BeTrue();
        sut.HasPermission(Permissions.Roles.Assign).Should().BeTrue();
        sut.HasPermission(Permissions.Hangfire.Dashboard).Should().BeFalse();
    }

    [Fact]
    public void Organization_Admin_Cannot_Have_Hangfire_Dashboard()
    {
        var sut = CreateStaff(AppRoles.OrganizationAdmin);
        sut.HasPermission(Permissions.Availability.ManageOrganization).Should().BeTrue();
        sut.HasPermission(Permissions.Hangfire.Dashboard).Should().BeFalse();
    }

    [Fact]
    public void Platform_Admin_Has_Broad_Permissions_Including_Dashboard()
    {
        var sut = CreatePermissionService(
            authenticated: true,
            roles: [AppRoles.PlatformAdmin],
            staffActive: false,
            staffRole: string.Empty);

        sut.HasPermission(Permissions.Hangfire.Dashboard).Should().BeTrue();
        sut.HasPermission(Permissions.Roles.Assign).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Read).Should().BeTrue();
    }

    [Fact]
    public void Permission_Alone_Does_Not_Imply_Tenant_Bypass()
    {
        // PLATFORM_ADMIN has appointments.read, but tenant access still requires explicit bypass.
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        var tenant = new TenantAccessService(
            user,
            new FakeCurrentStaff(),
            new FakeCurrentPatient(),
            new NoOpAuthorizationAuditLogger(),
            NullLogger<TenantAccessService>.Instance);

        tenant.CanAccessClinic(Guid.NewGuid()).Should().BeFalse();
        tenant.CanAccessClinic(Guid.NewGuid(), PlatformAdminBypass.Explicit).Should().BeTrue();
    }

    private static IPermissionService CreateStaff(string role) =>
        CreatePermissionService(true, [role], true, role);

    private static IPermissionService CreatePermissionService(
        bool authenticated,
        IReadOnlyList<string> roles,
        bool staffActive,
        string staffRole,
        bool linkedPatient = false)
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = authenticated,
            UserId = authenticated ? Guid.NewGuid() : null,
            Roles = roles,
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = staffActive,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            Role = staffRole,
        };
        var patient = new FakeCurrentPatient
        {
            HasLinkedPatient = linkedPatient,
            PatientId = linkedPatient ? Guid.NewGuid() : null,
        };

        return new PermissionService(
            user,
            staff,
            patient,
            new NoOpAuthorizationAuditLogger());
    }
}

public sealed class RoleAssignmentAuthorizationTests
{
    [Fact]
    public void Clinic_Admin_Cannot_Grant_Organization_Or_Platform_Admin()
    {
        var sut = CreateSut();
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var org = Guid.NewGuid();
        var clinic = Guid.NewGuid();

        sut.CanAssignRole(new RoleAssignmentRequest(
            actor, AppRoles.ClinicAdmin, org, clinic, target, AppRoles.OrganizationAdmin, org, clinic))
            .Should().BeFalse();

        sut.CanAssignRole(new RoleAssignmentRequest(
            actor, AppRoles.ClinicAdmin, org, clinic, target, AppRoles.PlatformAdmin, org, clinic))
            .Should().BeFalse();

        sut.CanAssignRole(new RoleAssignmentRequest(
            actor, AppRoles.ClinicAdmin, org, clinic, target, AppRoles.Doctor, org, clinic))
            .Should().BeTrue();
    }

    [Fact]
    public void Organization_Admin_Cannot_Grant_Platform_Admin()
    {
        var sut = CreateSut();
        var org = Guid.NewGuid();
        sut.CanAssignRole(new RoleAssignmentRequest(
            Guid.NewGuid(), AppRoles.OrganizationAdmin, org, Guid.NewGuid(),
            Guid.NewGuid(), AppRoles.PlatformAdmin, org, Guid.NewGuid()))
            .Should().BeFalse();
    }

    [Fact]
    public void Users_Cannot_Elevate_Themselves()
    {
        var sut = CreateSut();
        var id = Guid.NewGuid();
        var org = Guid.NewGuid();
        var clinic = Guid.NewGuid();
        sut.CanAssignRole(new RoleAssignmentRequest(
            id, AppRoles.ClinicAdmin, org, clinic, id, AppRoles.Doctor, org, clinic))
            .Should().BeFalse();
    }

    [Fact]
    public void Unknown_Target_Role_Rejected()
    {
        var sut = CreateSut();
        var org = Guid.NewGuid();
        var clinic = Guid.NewGuid();
        sut.CanAssignRole(new RoleAssignmentRequest(
            Guid.NewGuid(), AppRoles.ClinicAdmin, org, clinic,
            Guid.NewGuid(), "SUPERUSER", org, clinic))
            .Should().BeFalse();
    }

    [Fact]
    public void Patient_Role_Cannot_Mix_With_Staff_Membership()
    {
        var sut = CreateSut();
        var org = Guid.NewGuid();
        sut.CanAssignRole(new RoleAssignmentRequest(
            Guid.NewGuid(), AppRoles.OrganizationAdmin, org, null,
            Guid.NewGuid(), AppRoles.Patient, org, null,
            TargetHasStaffMembership: true))
            .Should().BeFalse();
    }

    [Fact]
    public void Cross_Clinic_Assignment_Denied_For_Clinic_Admin()
    {
        var sut = CreateSut();
        var org = Guid.NewGuid();
        sut.CanAssignRole(new RoleAssignmentRequest(
            Guid.NewGuid(), AppRoles.ClinicAdmin, org, Guid.NewGuid(),
            Guid.NewGuid(), AppRoles.Doctor, org, Guid.NewGuid()))
            .Should().BeFalse();
    }

    private static RoleAssignmentAuthorizationService CreateSut() =>
        new(new NoOpAuthorizationAuditLogger());
}
