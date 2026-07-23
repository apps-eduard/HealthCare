using FluentAssertions;
using HealthCare.Application.Authorization;

namespace HealthCare.UnitTests;

public sealed class CurrentUserAbstractionTests
{
    [Fact]
    public void Anonymous_Current_User_Has_No_Identity()
    {
        var user = new FakeCurrentUser();

        user.IsAuthenticated.Should().BeFalse();
        user.UserId.Should().BeNull();
        user.Roles.Should().BeEmpty();
        user.OrganizationId.Should().BeNull();
        user.ClinicId.Should().BeNull();
        user.PatientId.Should().BeNull();
        user.IsInRole("PLATFORM_ADMIN").Should().BeFalse();
    }

    [Fact]
    public void Authenticated_Current_User_Exposes_Resolved_Scope()
    {
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Email = "doc@example.com",
            Roles = ["DOCTOR"],
            OrganizationId = orgId,
            ClinicId = clinicId,
            StaffMemberId = Guid.NewGuid(),
        };

        user.IsAuthenticated.Should().BeTrue();
        user.UserId.Should().Be(userId);
        user.Email.Should().Be("doc@example.com");
        user.IsInRole("DOCTOR").Should().BeTrue();
        user.OrganizationId.Should().Be(orgId);
        user.ClinicId.Should().Be(clinicId);
        user.PatientId.Should().BeNull();
    }

    [Fact]
    public void Missing_Optional_Claims_Remain_Null()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = ["PLATFORM_ADMIN"],
        };

        user.OrganizationId.Should().BeNull();
        user.ClinicId.Should().BeNull();
        user.PatientId.Should().BeNull();
        user.StaffMemberId.Should().BeNull();
    }
}
