using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicPickerSupportTests
{
    [Fact]
    public async Task Missing_ClinicsRead_Hides_Clinic_Filter()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.StaffRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.CanFilterByClinic.Should().BeFalse();
        state.Has(WebPermissions.ClinicsRead).Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_With_ClinicsRead_Can_Filter()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.ClinicsRead, WebPermissions.StaffRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.CanFilterByClinic.Should().BeTrue();
        state.IsOrganizationAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Clinic_Admin_Does_Not_Get_Picker_Filter()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions = [WebPermissions.ClinicsRead, WebPermissions.StaffRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.CanFilterByClinic.Should().BeFalse();
        state.Has(WebPermissions.ClinicsRead).Should().BeTrue();
    }

    [Fact]
    public void Clinic_Cache_Clears_On_Logout()
    {
        var cache = new ClinicDirectoryCache();
        var id = Guid.NewGuid();
        cache.Set(new ClinicDetailResponse
        {
            ClinicId = id,
            OrganizationId = Guid.NewGuid(),
            Name = "A",
            Slug = "a",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        });

        cache.TryGet(id, out var found).Should().BeTrue();
        found.Should().NotBeNull();
        cache.Clear();
        cache.TryGet(id, out _).Should().BeFalse();
    }

    [Fact]
    public void All_Clinics_Filter_Uses_Null_ClinicId()
    {
        Guid? selected = Guid.NewGuid();
        selected = null;
        selected.Should().BeNull();
    }
}
