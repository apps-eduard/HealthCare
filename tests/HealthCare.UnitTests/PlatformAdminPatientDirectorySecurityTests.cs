using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Patients;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

/// <summary>
/// Regression: PLATFORM_ADMIN staff Patient Directory vs Patient self-service boundaries.
/// </summary>
public sealed class PlatformAdminPatientDirectorySecurityTests
{
    [Fact]
    public void Platform_Admin_Receives_Staff_Patient_Directory_Permissions()
    {
        var sut = CreatePlatformAdmin();

        sut.HasPermission(Permissions.Patients.Search).Should().BeTrue();
        sut.HasPermission(Permissions.Patients.Read).Should().BeTrue();
        sut.HasPermission(Permissions.Patients.UpdateClinicStatus).Should().BeTrue();
        sut.HasPermission(Permissions.MedicalNotes.Read).Should().BeFalse();
    }

    [Fact]
    public async Task Platform_Admin_UpdateOwnProfile_Catalog_Grant_Does_Not_Imply_Patient_Self_Scope()
    {
        // Catalog currently includes patients.update_own_profile for PLATFORM_ADMIN (broad historical grant).
        // Endpoints still require PatientSelfScope (PATIENT role + linked Patient) — see PatientsController.
        var sut = CreatePlatformAdmin();
        sut.HasPermission(Permissions.Patients.UpdateOwnProfile).Should().BeTrue(
            because: "matrix currently grants update_own_profile; removal is optional hygiene only");

        RolePermissionMatrix.RoleHasPermission(AppRoles.PlatformAdmin, Permissions.Patients.UpdateOwnProfile)
            .Should().BeTrue();

        // Without PATIENT + linkage, self-scope handler must fail closed.
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        var handler = new PatientSelfScopeHandler(
            new FakeCurrentPatient { HasLinkedPatient = false },
            user,
            NullLogger<PatientSelfScopeHandler>.Instance);

        var authContext = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext(
            [new PatientSelfScopeRequirement()],
            new System.Security.Claims.ClaimsPrincipal(),
            resource: null);

        await handler.HandleAsync(authContext);
        authContext.HasSucceeded.Should().BeFalse(
            because: "PLATFORM_ADMIN staff permissions must not satisfy PatientSelfScope");
    }

    [Fact]
    public void Staff_Patient_Directory_Contracts_Exclude_Clinical_Note_Fields()
    {
        AssertNoClinicalNoteProperties(typeof(StaffPatientSummaryResponse));
        AssertNoClinicalNoteProperties(typeof(StaffPatientDetailResponse));
        AssertNoClinicalNoteProperties(typeof(StaffPatientLookupItemResponse));
        AssertNoClinicalNoteProperties(typeof(StaffPatientClinicEnrollmentResponse));
    }

    [Fact]
    public async Task Platform_Admin_Search_And_Detail_Require_Bypass_And_Clinic_And_Exclude_Note_Bodies()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        var sut = new StaffPatientService(
            harness.Db,
            user,
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger(),
            NullLogger<StaffPatientService>.Instance);

        await FluentActions.Awaiting(() => sut.SearchAsync(
                new StaffPatientSearchRequest { ClinicId = data.ClinicAId }))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => sut.SearchAsync(
                new StaffPatientSearchRequest(),
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<AuthorizationException>();

        var search = await sut.SearchAsync(
            new StaffPatientSearchRequest { ClinicId = data.ClinicAId },
            PlatformAdminBypass.Explicit);
        search.Items.Should().Contain(i => i.PatientId == data.PatientInAId);

        var detail = await sut.GetByPatientIdAsync(
            data.PatientInAId,
            data.ClinicAId,
            PlatformAdminBypass.Explicit);
        detail.PatientId.Should().Be(data.PatientInAId);
        detail.Enrollments.Should().NotBeEmpty();

        // Shape guard: directory DTOs expose demographics/enrollment only.
        detail.GetType().GetProperty("Plan").Should().BeNull();
        detail.GetType().GetProperty("Subjective").Should().BeNull();
        detail.GetType().GetProperty("NoteBody").Should().BeNull();
        detail.GetType().GetProperty("MedicalNotes").Should().BeNull();
    }

    private static IPermissionService CreatePlatformAdmin()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        return new PermissionService(
            user,
            new FakeCurrentStaff { HasActiveMembership = false },
            new FakeCurrentPatient { HasLinkedPatient = false },
            new NoOpAuthorizationAuditLogger());
    }

    private static void AssertNoClinicalNoteProperties(Type type)
    {
        var names = type.GetProperties().Select(p => p.Name).ToArray();
        names.Should().NotContain(n =>
            n.Contains("Note", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Subjective", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Objective", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Assessment", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Plan", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Amendment", StringComparison.OrdinalIgnoreCase));
    }
}
