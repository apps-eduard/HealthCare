using FluentAssertions;
using HealthCare.Domain.Identity;

namespace HealthCare.UnitTests;

public sealed class AppRolesTests
{
    [Fact]
    public void All_Contains_Exactly_Documented_Roles()
    {
        AppRoles.All.Should().BeEquivalentTo(
        [
            "PLATFORM_ADMIN",
            "ORGANIZATION_ADMIN",
            "CLINIC_ADMIN",
            "DOCTOR",
            "NURSE",
            "RECEPTIONIST",
            "PATIENT",
        ]);
    }

    [Fact]
    public void All_Has_No_Duplicates()
    {
        AppRoles.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Constants_Match_Documented_Names()
    {
        AppRoles.PlatformAdmin.Should().Be("PLATFORM_ADMIN");
        AppRoles.OrganizationAdmin.Should().Be("ORGANIZATION_ADMIN");
        AppRoles.ClinicAdmin.Should().Be("CLINIC_ADMIN");
        AppRoles.Doctor.Should().Be("DOCTOR");
        AppRoles.Nurse.Should().Be("NURSE");
        AppRoles.Receptionist.Should().Be("RECEPTIONIST");
        AppRoles.Patient.Should().Be("PATIENT");
    }
}
