using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Appointments;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminAppointmentsUiTests
{
    [Fact]
    public async Task Clinic_Admin_Sees_Complete_Without_Clinic_Picker()
    {
        var state = await ClinicAdminStateAsync();

        AppointmentDirectoryPermissionRules.CanView(state).Should().BeTrue();
        AppointmentDirectoryPermissionRules.ShowClinicPicker(state).Should().BeFalse();
        AppointmentDirectoryPermissionRules.CanComplete(state).Should().BeTrue();
        AppointmentDirectoryPageCopy.QueueSubtitle(state, "Asia/Riyadh").Should().Contain("Your clinic");
        AppointmentActionRules.CanShow(AppointmentUiAction.Complete, "CheckedIn", state).Should().BeTrue();
        AppointmentActionRules.CanShow(AppointmentUiAction.NoShow, "Confirmed", state).Should().BeTrue();
        AppointmentActionRules.CanShow(AppointmentUiAction.Complete, "Confirmed", state).Should().BeFalse();
        state.Has("medical_notes.read").Should().BeFalse();
        state.Has(WebPermissions.OrganizationProfileRead).Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Keeps_Clinic_Picker_Without_Complete()
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
                WebPermissions.AppointmentsCancel,
                WebPermissions.AppointmentsNoShow,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        AppointmentDirectoryPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        AppointmentDirectoryPermissionRules.CanComplete(state).Should().BeFalse();
        AppointmentActionRules.CanShow(AppointmentUiAction.Complete, "CheckedIn", state).Should().BeFalse();
        AppointmentDirectoryPageCopy.QueueSubtitle(state, null).Should().Contain("Organization");
    }

    [Fact]
    public async Task Platform_Admin_Keeps_Explicit_Context_Behavior()
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
                WebPermissions.AppointmentsComplete,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = false,
        });

        AppointmentDirectoryPermissionRules.ShowClinicPicker(state).Should().BeTrue();
        AppointmentDirectoryPageCopy.QueueSubtitle(state, null).Should().Contain("selected");
    }

    [Fact]
    public void Appointments_Pages_Are_Actor_Aware_For_Clinic_Admin()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var queue = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Appointments.razor"));
        var calendar = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "AppointmentsCalendar.razor"));
        var create = File.ReadAllText(Path.Combine(webRoot, "Components", "Appointments", "CreateAppointmentDialog.razor"));
        var detail = File.ReadAllText(Path.Combine(webRoot, "Components", "Appointments", "AppointmentDetailDialog.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));

        queue.Should().Contain("AppointmentDirectoryPageCopy.QueueSubtitle");
        queue.Should().Contain("AppointmentDirectoryPermissionRules.ShowClinicPicker");
        queue.Should().Contain("appointments-clinic-caption");
        queue.Should().Contain("aria-label=\"Clinic appointments\"");
        queue.Should().NotContain("@inject HttpClient");
        queue.Should().NotContain("medical_notes");
        queue.Should().NotContain("MedicalNote");

        calendar.Should().Contain("AppointmentDirectoryPageCopy.CalendarSubtitle");
        calendar.Should().Contain("appointments-calendar-clinic-caption");

        create.Should().Contain("create-appointment-clinic-caption");
        create.Should().NotContain("ClinicPicker Label=\"Clinic\" AllowClear=\"false\" Required=\"false\" Disabled=\"true\"");

        detail.Should().Contain("AppointmentActionRules.GetVisibleActions");
        detail.Should().Contain("ExpectedVersion");
        detail.Should().Contain("CompleteAsync");
        detail.Should().Contain("MarkNoShowAsync");
        detail.Should().NotContain("MedicalNote");

        layout.Should().Contain("RouterLink=\"/appointments\"");
        layout.Should().Contain("WebPermissions.AppointmentsRead");
    }

    [Fact]
    public void Safe_Error_And_Concurrency_Messages()
    {
        AppointmentProblemMessages.ToUserMessage(new ApiProblemException(400, "Bad", "raw", "appointment.invalid_request"))
            .Should().NotContain("raw");
        AppointmentProblemMessages.ToUserMessage(new ApiProblemException(
            403, "Forbidden", "x", "authorization.permission_denied"))
            .Should().Contain("permission");
        AppointmentProblemMessages.IsConcurrencyConflict(new ApiProblemException(
            409, "Conflict", null, "appointment.concurrency_conflict")).Should().BeTrue();
        AppointmentProblemMessages.ToUserMessage(new ApiProblemException(
            409, "Conflict", null, "appointment.concurrency_conflict"))
            .Should().Contain("Reload");
    }

    private static async Task<PermissionState> ClinicAdminStateAsync()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "clinicadmin@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.AppointmentsRead,
                WebPermissions.AppointmentsCreate,
                WebPermissions.AppointmentsConfirm,
                WebPermissions.AppointmentsCheckIn,
                WebPermissions.AppointmentsComplete,
                WebPermissions.AppointmentsNoShow,
                WebPermissions.AppointmentsCancel,
                WebPermissions.AppointmentsReschedule,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        return state;
    }
}
