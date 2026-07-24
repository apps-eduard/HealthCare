using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Staff;
using HealthCare.Contracts.Staff;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

/// <summary>
/// Clinic Admin staff-management regression coverage for CA-3.
/// Avoids duplicating behaviors already asserted in StaffManagementServiceTests.
/// </summary>
public sealed class ClinicAdminStaffManagementTests
{
    [Fact]
    public void Clinic_Admin_Can_Assign_Allowed_Clinic_Roles_And_Not_Patient()
    {
        var sut = new RoleAssignmentAuthorizationService(new NoOpAuthorizationAuditLogger());
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();
        var org = Guid.NewGuid();
        var clinic = Guid.NewGuid();

        foreach (var role in new[]
                 {
                     AppRoles.ClinicAdmin, AppRoles.Doctor, AppRoles.Nurse, AppRoles.Receptionist,
                 })
        {
            sut.CanAssignRole(new RoleAssignmentRequest(
                    actor, AppRoles.ClinicAdmin, org, clinic, target, role, org, clinic))
                .Should().BeTrue($"CLINIC_ADMIN should assign {role}");
        }

        sut.CanAssignRole(new RoleAssignmentRequest(
                actor, AppRoles.ClinicAdmin, org, clinic, target, AppRoles.Patient, org, clinic,
                TargetHasStaffMembership: true))
            .Should().BeFalse();
        sut.CanAssignRole(new RoleAssignmentRequest(
                actor, AppRoles.ClinicAdmin, org, clinic, target, AppRoles.OrganizationAdmin, org, clinic))
            .Should().BeFalse();
        sut.CanAssignRole(new RoleAssignmentRequest(
                actor, AppRoles.ClinicAdmin, org, clinic, target, AppRoles.PlatformAdmin, org, clinic))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Clinic_Admin_Can_Assign_Doctor_Nurse_And_Clinic_Admin()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-roles@test.local");
        var nurse = await h.SeedStaffAsync(AppRoles.Nurse, h.ClinicA.Id, "nurse-target@test.local");
        var receptionist = await h.SeedStaffAsync(AppRoles.Receptionist, h.ClinicA.Id, "recv-target@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-target@test.local");
        var sut = h.CreateService(clinicAdmin);

        await sut.AssignRoleAsync(nurse.Staff.Id, AppRoles.Doctor);
        (await sut.GetByIdAsync(nurse.Staff.Id)).Role.Should().Be(AppRoles.Doctor);

        await sut.AssignRoleAsync(receptionist.Staff.Id, AppRoles.Nurse);
        (await sut.GetByIdAsync(receptionist.Staff.Id)).Role.Should().Be(AppRoles.Nurse);

        await sut.AssignRoleAsync(doctor.Staff.Id, AppRoles.ClinicAdmin);
        (await sut.GetByIdAsync(doctor.Staff.Id)).Role.Should().Be(AppRoles.ClinicAdmin);
    }

    [Fact]
    public async Task Clinic_Admin_Cannot_Assign_Platform_Admin_Or_Patient()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-forbid@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-forbid@test.local");
        var sut = h.CreateService(clinicAdmin);

        await FluentActions.Awaiting(() => sut.AssignRoleAsync(doctor.Staff.Id, AppRoles.PlatformAdmin))
            .Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.RoleAssignmentDenied);

        StaffManagementException? patientEx = null;
        try
        {
            await sut.AssignRoleAsync(doctor.Staff.Id, AppRoles.Patient);
        }
        catch (StaffManagementException ex)
        {
            patientEx = ex;
        }

        patientEx.Should().NotBeNull();
        patientEx!.ErrorCode.Should().BeOneOf(
            StaffErrorCodes.RoleAssignmentDenied,
            StaffErrorCodes.InvalidRole);
    }

    [Fact]
    public async Task Cross_Clinic_Staff_Mutation_Is_Hidden()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-cross@test.local");
        var otherDoctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-other@test.local");
        var sut = h.CreateService(clinicAdmin);

        await FluentActions.Awaiting(() => sut.GetByIdAsync(otherDoctor.Staff.Id))
            .Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.NotFound);

        await FluentActions.Awaiting(() => sut.UpdateAsync(
                otherDoctor.Staff.Id,
                new UpdateStaffRequest { ExpectedVersion = otherDoctor.Staff.Version, FirstName = "Hacked" }))
            .Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.NotFound);

        await FluentActions.Awaiting(() => sut.RevokeSessionsAsync(
                otherDoctor.Staff.Id,
                new RevokeStaffSessionsRequest()))
            .Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.NotFound);
    }

    [Fact]
    public async Task Last_Clinic_Admin_Cannot_Be_Deactivated()
    {
        await using var h = await StaffHarness.CreateAsync();
        var soleAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-sole@test.local");
        var peer = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-peer@test.local");
        var sut = h.CreateService(soleAdmin);

        await sut.DeactivateAsync(peer.Staff.Id, new StaffActivationRequest
        {
            ExpectedVersion = peer.Staff.Version,
        });

        var refreshed = await h.Db.StaffMembers.AsNoTracking().SingleAsync(s => s.Id == soleAdmin.Staff.Id);
        // Need another actor with permission — use org admin from same org.
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-last-ca@test.local");
        var orgSut = h.CreateService(orgAdmin);

        await FluentActions.Awaiting(() => orgSut.DeactivateAsync(
                refreshed.Id,
                new StaffActivationRequest { ExpectedVersion = refreshed.Version }))
            .Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.LastAdminProtected);
    }

    [Fact]
    public async Task Clinic_Admin_Self_Deactivation_Denied()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-self@test.local");
        await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-backup@test.local");
        var sut = h.CreateService(clinicAdmin);

        await FluentActions.Awaiting(() => sut.DeactivateAsync(
                clinicAdmin.Staff.Id,
                new StaffActivationRequest { ExpectedVersion = clinicAdmin.Staff.Version }))
            .Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.SelfDeactivationDenied);
    }

    [Fact]
    public async Task Inactive_Membership_Is_Denied()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-inactive@test.local");
        clinicAdmin.Staff.IsActive = false;
        await h.Db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = clinicAdmin.User.Id,
            Email = clinicAdmin.User.Email,
            Roles = [AppRoles.ClinicAdmin],
            OrganizationId = clinicAdmin.Staff.OrganizationId,
            ClinicId = clinicAdmin.Staff.ClinicId,
            StaffMemberId = clinicAdmin.Staff.Id,
        };
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        var sut = h.BuildService(currentUser, currentStaff);

        await FluentActions.Awaiting(() => sut.SearchAsync(new StaffSearchRequest()))
            .Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Clinic_Admin_Password_Reset_Does_Not_Expose_Token()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-pwd@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-pwd-ca@test.local");
        var sut = h.CreateService(clinicAdmin);

        var response = await sut.RequestPasswordResetAsync(doctor.Staff.Id, new StaffPasswordResetRequest());
        response.Message.Should().NotBeNullOrWhiteSpace();
        typeof(StaffPasswordResetResponse).GetProperty("Token").Should().BeNull();
        typeof(StaffPasswordResetResponse).GetProperty("ResetToken").Should().BeNull();
        response.Message.ToLowerInvariant().Should().NotContain("token");
        JsonDoesNotContainSecrets(System.Text.Json.JsonSerializer.Serialize(response));
    }

    private static void JsonDoesNotContainSecrets(string json)
    {
        json.ToLowerInvariant().Should().NotContain("resettoken");
        json.ToLowerInvariant().Should().NotContain("refreshtoken");
        json.ToLowerInvariant().Should().NotContain("temppass");
        json.Should().NotMatchRegex("(?i)\"token\"\\s*:");
    }
}
