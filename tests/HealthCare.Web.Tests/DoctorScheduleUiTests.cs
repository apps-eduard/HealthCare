using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Appointments;
using HealthCare.Web.Auth;
using HealthCare.Web.Availability;

namespace HealthCare.Web.Tests;

public sealed class DoctorScheduleUiTests
{
    [Fact]
    public async Task Doctor_Locks_Doctor_Filter_To_Self_On_Queue_And_Calendar()
    {
        var staffId = Guid.NewGuid();
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doctor@test.local",
            Roles = [WebRoles.Doctor],
            Permissions =
            [
                WebPermissions.AppointmentsRead,
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageSelf,
                WebPermissions.DoctorDashboardRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            StaffMemberId = staffId,
        });

        AppointmentDirectoryPermissionRules.LockDoctorFilterToSelf(state).Should().BeTrue();
        AppointmentDirectoryPermissionRules.CanCreate(state).Should().BeFalse();
        AvailabilityPermissionRules.IsSelfOnly(state).Should().BeTrue();
        AppointmentDirectoryPageCopy.QueueSubtitle(state, "Asia/Riyadh")
            .Should().Contain("Your assigned appointments");
        AppointmentDirectoryPageCopy.CalendarSubtitle(state, "Asia/Riyadh")
            .Should().Contain("Your assigned schedule");
    }

    [Fact]
    public async Task Clinic_Admin_Keeps_Doctor_Picker()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.AppointmentsRead,
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageClinic,
                WebPermissions.ClinicDashboardRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            StaffMemberId = Guid.NewGuid(),
        });

        AppointmentDirectoryPermissionRules.LockDoctorFilterToSelf(state).Should().BeFalse();
        AvailabilityPermissionRules.IsSelfOnly(state).Should().BeFalse();
    }

    [Fact]
    public async Task Platform_Admin_Does_Not_Lock_To_Self()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions =
            [
                WebPermissions.AppointmentsRead,
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageSelf,
                WebPermissions.AvailabilityManageClinic,
            ],
            HasActiveStaffMembership = false,
        });

        AppointmentDirectoryPermissionRules.LockDoctorFilterToSelf(state).Should().BeFalse();
    }

    [Fact]
    public void Appointments_Pages_Hide_Doctor_Select_For_Self_Lock()
    {
        var queue = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Components", "Pages", "Appointments.razor"));
        queue.Should().Contain("LockDoctorFilterToSelf");
        queue.Should().Contain("appointments-doctor-self");
        queue.Should().Contain("queue-doctor-self");
        queue.Should().Contain("ApplyDoctorSelfScope");
        queue.Should().Contain("Showing only appointments assigned to you");
        queue.Should().Contain("WebPermissions.AppointmentsCreate");

        var calendar = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Components", "Pages", "AppointmentsCalendar.razor"));
        calendar.Should().Contain("LockDoctorFilterToSelf");
        calendar.Should().Contain("calendar-doctor-self");
        calendar.Should().Contain("ApplyDoctorSelfScope");
        calendar.Should().Contain("Showing only appointments assigned to you");

        var availability = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Components", "Pages", "Availability.razor"));
        availability.Should().Contain("My Availability");
        availability.Should().Contain("_selfOnly");
        availability.Should().Contain("availability-doctor-self");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HealthCare.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
