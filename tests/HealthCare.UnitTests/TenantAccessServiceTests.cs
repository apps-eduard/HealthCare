using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class TenantAccessServiceTests
{
    [Fact]
    public void Anonymous_Cannot_Access_Organization_Or_Clinic()
    {
        var user = new FakeCurrentUser();
        var staff = new FakeCurrentStaff();
        var patient = new FakeCurrentPatient();
        var sut = CreateSut(user, staff, patient);

        sut.CanAccessOrganization(Guid.NewGuid()).Should().BeFalse();
        sut.CanAccessClinic(Guid.NewGuid()).Should().BeFalse();
        sut.CanAccessPatient(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Organization_Access_Allowed_For_Matching_Staff_Scope()
    {
        var orgId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.ClinicAdmin],
            OrganizationId = orgId,
            ClinicId = clinicId,
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgId,
            ClinicId = clinicId,
            Role = AppRoles.ClinicAdmin,
        };

        var sut = CreateSut(user, staff, new FakeCurrentPatient());

        sut.CanAccessOrganization(orgId).Should().BeTrue();
        sut.CanAccessOrganization(Guid.NewGuid()).Should().BeFalse();
        var deny = () => sut.EnsureCanAccessOrganization(Guid.NewGuid());
        deny.Should().Throw<AuthorizationException>()
            .Which.ErrorCode.Should().Be("authz.organization_access_denied");
    }

    [Fact]
    public void Clinic_Access_Allowed_For_Matching_Staff_Scope()
    {
        var orgId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
            OrganizationId = orgId,
            ClinicId = clinicId,
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgId,
            ClinicId = clinicId,
            Role = AppRoles.Doctor,
        };

        var sut = CreateSut(user, staff, new FakeCurrentPatient());

        sut.CanAccessClinic(clinicId).Should().BeTrue();
        sut.CanAccessClinic(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void PlatformAdmin_Bypass_Requires_Explicit_Flag()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        var sut = CreateSut(user, new FakeCurrentStaff(), new FakeCurrentPatient());
        var foreignOrg = Guid.NewGuid();

        sut.CanAccessOrganization(foreignOrg).Should().BeFalse();
        sut.CanAccessOrganization(foreignOrg, PlatformAdminBypass.Explicit).Should().BeTrue();
        sut.CanAccessClinic(Guid.NewGuid(), PlatformAdminBypass.Explicit).Should().BeTrue();
    }

    [Fact]
    public void Patient_Without_Linkage_Is_Denied()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Patient],
        };
        var patient = new FakeCurrentPatient { HasLinkedPatient = false, PatientId = null };
        var sut = CreateSut(user, new FakeCurrentStaff(), patient);

        var act = () => sut.EnsureCanAccessPatient(Guid.NewGuid());
        act.Should().Throw<AuthorizationException>()
            .Which.ErrorCode.Should().Be("authz.missing_patient_linkage");
    }

    [Fact]
    public void Patient_Self_Access_Allowed_When_Linked()
    {
        var patientId = Guid.NewGuid();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Patient],
            PatientId = patientId,
        };
        var patient = new FakeCurrentPatient { HasLinkedPatient = true, PatientId = patientId };
        var sut = CreateSut(user, new FakeCurrentStaff(), patient);

        sut.CanAccessPatient(patientId).Should().BeTrue();
        sut.CanAccessPatient(Guid.NewGuid()).Should().BeFalse();
        var act = () => sut.EnsureCanAccessPatient(Guid.NewGuid());
        act.Should().Throw<AuthorizationException>()
            .Which.ErrorCode.Should().Be("authz.patient_self_scope_denied");
    }

    [Fact]
    public void Inactive_Staff_Membership_Denies_Tenant_Access()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
        };
        var staff = new FakeCurrentStaff { HasActiveMembership = false };
        var sut = CreateSut(user, staff, new FakeCurrentPatient());

        sut.CanAccessOrganization(Guid.NewGuid()).Should().BeFalse();
        sut.CanAccessClinic(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void PlatformAdmin_Patient_Bypass_Requires_Explicit_Flag()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        var sut = CreateSut(user, new FakeCurrentStaff(), new FakeCurrentPatient());
        var foreignPatient = Guid.NewGuid();

        sut.CanAccessPatient(foreignPatient).Should().BeFalse();
        sut.CanAccessPatient(foreignPatient, PlatformAdminBypass.Explicit).Should().BeTrue();
    }

    private static TenantAccessService CreateSut(
        ICurrentUser user,
        ICurrentStaff staff,
        ICurrentPatient patient) =>
        new(user, staff, patient, NullLogger<TenantAccessService>.Instance);
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }

    public Guid? UserId { get; set; }

    public string? Email { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    public Guid? OrganizationId { get; set; }

    public Guid? ClinicId { get; set; }

    public Guid? PatientId { get; set; }

    public Guid? StaffMemberId { get; set; }

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);
}

internal sealed class FakeCurrentStaff : ICurrentStaff
{
    public bool HasActiveMembership { get; set; }

    public Guid StaffMemberId { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClinicId { get; set; }

    public string Role { get; set; } = string.Empty;
}

internal sealed class FakeCurrentPatient : ICurrentPatient
{
    public bool HasLinkedPatient { get; set; }

    public Guid? PatientId { get; set; }
}
