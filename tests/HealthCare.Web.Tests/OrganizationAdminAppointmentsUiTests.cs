using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Appointments;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminAppointmentsUiTests
{
    [Fact]
    public void Queue_And_Calendar_Builders_Map_Filters()
    {
        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();

        var queue = AppointmentListQueryBuilder.BuildQueue(
            new DateTime(2026, 7, 23),
            new DateTime(2026, 7, 24),
            status: null,
            doctorId,
            clinicId,
            page: 1,
            pageSize: 50,
            clinicTimeZoneId: "UTC");

        queue.Status.Should().BeNull();
        queue.DoctorStaffMemberId.Should().Be(doctorId);
        queue.ClinicId.Should().Be(clinicId);
        queue.PageSize.Should().Be(50);
        queue.FromUtc.Should().NotBeNull();
        queue.ToUtc.Should().NotBeNull();

        var calendar = AppointmentListQueryBuilder.BuildCalendar(
            new DateOnly(2026, 7, 20),
            new DateOnly(2026, 7, 26),
            view: "week",
            status: "Confirmed",
            doctorId,
            clinicId,
            pageSize: 200,
            clinicTimeZoneId: "UTC");

        calendar.View.Should().Be("week");
        calendar.Status.Should().Be("Confirmed");
        calendar.PageSize.Should().Be(200);
        calendar.FromUtc.Should().BeBefore(calendar.ToUtc);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Complete_Via_Permission_Gate()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.AppointmentsRead,
                WebPermissions.AppointmentsCreate,
                WebPermissions.AppointmentsConfirm,
                WebPermissions.AppointmentsCheckIn,
                WebPermissions.AppointmentsCancel,
                WebPermissions.AppointmentsReschedule,
                WebPermissions.AppointmentsNoShow,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.AppointmentsComplete).Should().BeFalse();
        state.IsOrganizationAdmin.Should().BeTrue();

        var actions = AppointmentActionRules.GetVisibleActions("CheckedIn", state);
        actions.Should().NotContain(AppointmentUiAction.Complete);
        actions.Should().Contain(AppointmentUiAction.NoShow);
    }

    [Fact]
    public void Appointment_Pages_Use_Queue_Calendar_And_Clinic_Context()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var queue = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Appointments.razor"));
        var calendar = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "AppointmentsCalendar.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));

        queue.Should().Contain("ListQueueAsync");
        queue.Should().Contain("IClinicWorkingContext");
        queue.Should().Contain("Appointment Queue");
        queue.Should().Contain("BuildQueue");
        queue.Should().NotContain("@inject HttpClient");

        calendar.Should().Contain("ListCalendarAsync");
        calendar.Should().Contain("IClinicWorkingContext");
        calendar.Should().Contain("BuildCalendar");
        calendar.Should().NotContain("@inject HttpClient");

        layout.Should().Contain("Appointment Queue");
        layout.Should().Contain("/appointments/calendar");
    }

    [Fact]
    public void Appointment_Client_Exposes_Queue_Calendar_And_Guid_Doctors()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "AppointmentApiClient.cs")));

        source.Should().Contain("api/v1/staff/appointments/queue");
        source.Should().Contain("api/v1/staff/appointments/calendar");
        source.Should().Contain("ListQueueAsync");
        source.Should().Contain("ListCalendarAsync");
        source.Should().Contain("staff/clinics/");
        source.Should().Contain("ListClinicDoctorsByIdAsync");
        source.Should().Contain("view=");
    }

    [Fact]
    public void Problem_Messages_Cover_Inactive_Clinic_And_Reschedule_Codes()
    {
        var inactive = new ApiProblemException(409, "Inactive", null, AppointmentErrorCodes.InactiveClinic);
        AppointmentProblemMessages.ToUserMessage(inactive).Should().Contain("inactive");

        var sameSlot = new ApiProblemException(400, "Same", null, AppointmentErrorCodes.RescheduleSameSlot);
        AppointmentProblemMessages.ToUserMessage(sameSlot).Should().Contain("different slot");

        var conflict = new ApiProblemException(409, "Conflict", null, AppointmentErrorCodes.ConcurrencyConflict);
        AppointmentProblemMessages.IsConcurrencyConflict(conflict).Should().BeTrue();
    }
}
