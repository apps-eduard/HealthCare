using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Availability;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminAvailabilityUiTests
{
    [Fact]
    public async Task Organization_Admin_Can_View_And_Manage_Availability()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.AvailabilityManageOrganization,
                WebPermissions.AvailabilityRead,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        AvailabilityPermissionRules.CanView(state).Should().BeTrue();
        AvailabilityPermissionRules.CanManage(state).Should().BeTrue();
        AvailabilityPermissionRules.IsSelfOnly(state).Should().BeFalse();
        state.CanFilterByClinic.Should().BeTrue();
    }

    [Fact]
    public async Task Read_Only_Permission_Opens_View_But_Not_Manage()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "nurse@test.local",
            Roles = ["NURSE"],
            Permissions = [WebPermissions.AvailabilityRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        AvailabilityPermissionRules.CanView(state).Should().BeTrue();
        AvailabilityPermissionRules.CanManage(state).Should().BeFalse();
    }

    [Fact]
    public void Effective_Composer_Applies_Windows_And_Exceptions_Per_Day()
    {
        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var monday = new DateOnly(2026, 7, 20); // Monday

        var windows = new[]
        {
            new DoctorAvailabilityResponse
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                DoctorStaffMemberId = doctorId,
                DayOfWeek = "Monday",
                StartLocalTime = "09:00",
                EndLocalTime = "12:00",
                SlotDurationMinutes = 30,
                EffectiveFrom = new DateOnly(2026, 1, 1),
                IsActive = true,
                Version = 1,
                ClinicTimeZoneId = "Asia/Riyadh",
            },
        };

        var exceptions = new[]
        {
            new DoctorAvailabilityExceptionResponse
            {
                Id = Guid.NewGuid(),
                DoctorStaffMemberId = doctorId,
                Date = monday,
                ExceptionType = "UnavailableFullDay",
                Reason = "Holiday",
                Version = 1,
            },
        };

        var days = AvailabilityEffectiveComposer.Build(windows, exceptions, monday, monday.AddDays(1));
        days.Should().HaveCount(2);
        days[0].Windows.Should().ContainSingle(w => w.StartLocalTime == "09:00");
        days[0].Exceptions.Should().ContainSingle(e => e.ExceptionType == "UnavailableFullDay");
        days[1].Windows.Should().BeEmpty();
        days[1].Exceptions.Should().BeEmpty();
    }

    [Fact]
    public void Effective_Composer_Caps_Range_At_Two_Weeks()
    {
        var from = new DateOnly(2026, 7, 1);
        var days = AvailabilityEffectiveComposer.Build([], [], from, from.AddDays(30));
        days.Should().HaveCount(14);
        days[^1].Date.Should().Be(from.AddDays(13));
    }

    [Fact]
    public void Availability_Page_Uses_Clinic_Context_Tabs_And_Guid_Doctors()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Availability.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));

        page.Should().Contain("Doctor Availability");
        page.Should().Contain("IClinicWorkingContext");
        page.Should().Contain("ListClinicDoctorsByIdAsync");
        page.Should().Contain("AvailabilityEffectiveComposer");
        page.Should().Contain("Weekly Schedule");
        page.Should().Contain("Effective Availability");
        page.Should().Contain("Slot Preview");
        page.Should().Contain("CanView");
        page.Should().Contain("_canManage");
        page.Should().Contain("ClinicId = _clinicId");
        page.Should().NotContain("@inject HttpClient");

        layout.Should().Contain("Doctor Availability");
        layout.Should().Contain("AvailabilityPermissionRules.CanView");
    }

    [Fact]
    public void Availability_Client_Exposes_Guid_Doctors_And_ClinicId_Query()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "DoctorAvailabilityApiClient.cs")));

        source.Should().Contain("ListClinicDoctorsByIdAsync");
        source.Should().Contain("api/v1/staff/clinics/");
        source.Should().Contain("clinicId=");
        source.Should().Contain("platformAdminBypass");
        source.Should().Contain("api/v1/staff/doctors/");
        source.Should().Contain("availability-exceptions");
        source.Should().Contain("available-slots");
    }

    [Fact]
    public void Dialogs_Pass_ClinicId_On_Mutations()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var window = File.ReadAllText(Path.Combine(webRoot, "Components", "Availability", "AvailabilityWindowDialog.razor"));
        var exception = File.ReadAllText(Path.Combine(webRoot, "Components", "Availability", "AvailabilityExceptionDialog.razor"));

        window.Should().Contain("ClinicId");
        window.Should().Contain("clinicId: Options.ClinicId");
        exception.Should().Contain("ClinicId");
        exception.Should().Contain("clinicId: Options.ClinicId");
    }
}
