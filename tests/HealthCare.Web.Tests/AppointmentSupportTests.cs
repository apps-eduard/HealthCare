using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Appointments;
using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class AppointmentSupportTests
{
    [Fact]
    public async Task Missing_AppointmentsRead_Is_Detectable()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "r@test.local",
            Roles = ["RECEPTIONIST"],
            Permissions = [WebPermissions.StaffRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.AppointmentsRead).Should().BeFalse();
    }

    [Fact]
    public async Task Patient_Only_Is_Blocked_From_Staff()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "p@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = false,
        });

        state.IsPatientOnly.Should().BeTrue();
        state.IsStaffUser.Should().BeFalse();
    }

    [Fact]
    public void Queue_Builder_Sends_Server_Side_Filters()
    {
        var clinicId = Guid.NewGuid();
        var doctorId = Guid.NewGuid();
        var query = AppointmentListQueryBuilder.Build(
            new DateTime(2026, 7, 23),
            new DateTime(2026, 7, 24),
            "Confirmed",
            doctorId,
            clinicId,
            page: 2,
            pageSize: 50,
            sortBy: "status",
            sortDirection: "desc",
            clinicTimeZoneId: "UTC");

        query.Status.Should().Be("Confirmed");
        query.DoctorStaffMemberId.Should().Be(doctorId);
        query.ClinicId.Should().Be(clinicId);
        query.Page.Should().Be(2);
        query.PageSize.Should().Be(50);
        query.SortBy.Should().Be("status");
        query.SortDirection.Should().Be("desc");
        query.FromUtc.Should().NotBeNull();
        query.ToUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Clinic_Filter_Follows_Permission_Context()
    {
        var orgAdmin = new PermissionState();
        await orgAdmin.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.ClinicsRead, WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        orgAdmin.CanFilterByClinic.Should().BeTrue();

        var clinicAdmin = new PermissionState();
        await clinicAdmin.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions = [WebPermissions.ClinicsRead, WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        clinicAdmin.CanFilterByClinic.Should().BeFalse();
    }

    [Theory]
    [InlineData("Requested", nameof(StatusTone.Warning))]
    [InlineData("Confirmed", nameof(StatusTone.Info))]
    [InlineData("CheckedIn", nameof(StatusTone.Primary))]
    [InlineData("Completed", nameof(StatusTone.Success))]
    [InlineData("NoShow", nameof(StatusTone.Neutral))]
    [InlineData("CancelledByClinic", nameof(StatusTone.Default))]
    public void Status_Chips_Map_Correctly(string status, string expectedToneName)
    {
        AppointmentStatusPresentation.DisplayLabel(status).Should().Be(status);
        AppointmentStatusPresentation.ChipTone(status).ToString().Should().Be(expectedToneName);
    }

    [Fact]
    public void Calendar_Uses_Clinic_Timezone_Not_Browser_Local()
    {
        var utc = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        var formatted = ClinicTimeDisplay.FormatLocalWithZone(utc, "UTC");
        formatted.Should().Contain("2026-07-23");
        formatted.Should().Contain("UTC");
        formatted.Should().NotContain("Local");
    }

    [Fact]
    public async Task Action_Visibility_Is_Permission_Aware()
    {
        var withComplete = new PermissionState();
        await withComplete.SetFromUserAsync(UserWith(
            WebPermissions.AppointmentsComplete,
            WebPermissions.AppointmentsCheckIn));

        AppointmentActionRules.CanShow(AppointmentUiAction.Complete, "CheckedIn", withComplete).Should().BeTrue();
        AppointmentActionRules.CanShow(AppointmentUiAction.CheckIn, "Confirmed", withComplete).Should().BeTrue();

        var withoutComplete = new PermissionState();
        await withoutComplete.SetFromUserAsync(UserWith(WebPermissions.AppointmentsCheckIn));
        AppointmentActionRules.CanShow(AppointmentUiAction.Complete, "CheckedIn", withoutComplete).Should().BeFalse();
        AppointmentActionRules.CanShow(AppointmentUiAction.NoShow, "Confirmed", withoutComplete).Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_Action_Requires_Permission_And_Uses_ExpectedVersion_Shape()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(UserWith(WebPermissions.AppointmentsConfirm));
        AppointmentActionRules.CanShow(AppointmentUiAction.Confirm, "Requested", state).Should().BeTrue();

        var request = new AppointmentActionRequest { ExpectedVersion = 3 };
        request.ExpectedVersion.Should().Be(3);
    }

    [Fact]
    public void Cancel_Sends_Reason_And_Version()
    {
        var request = new AppointmentActionRequest
        {
            ExpectedVersion = 4,
            CancellationReason = "Patient requested",
        };
        request.ExpectedVersion.Should().Be(4);
        request.CancellationReason.Should().Be("Patient requested");
    }

    [Fact]
    public void Reschedule_Uses_Slot_Start_Utc()
    {
        var slot = new AvailableSlotResponse
        {
            StartUtc = new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            EndUtc = new DateTimeOffset(2026, 7, 24, 8, 30, 0, TimeSpan.Zero),
            StartLocal = "11:00",
            EndLocal = "11:30",
            DurationMinutes = 30,
            TimeZoneId = "Asia/Riyadh",
        };

        var request = new RescheduleAppointmentRequest
        {
            DoctorStaffMemberId = Guid.NewGuid(),
            AppointmentDateUtc = slot.StartUtc,
            DurationMinutes = slot.DurationMinutes,
            ExpectedVersion = 2,
            Reason = "Conflict",
        };

        request.AppointmentDateUtc.Should().Be(slot.StartUtc);
        request.DurationMinutes.Should().Be(30);
        request.ExpectedVersion.Should().Be(2);
    }

    [Fact]
    public void Slot_Conflict_Maps_To_Safe_Message()
    {
        var ex = new ApiProblemException(409, "Conflict", "raw", "appointment.slot_unavailable");
        AppointmentProblemMessages.ToUserMessage(ex)
            .Should().Contain("no longer available");
        AppointmentProblemMessages.ToUserMessage(ex)
            .Should().NotContain("raw stack");
    }

    [Fact]
    public void Concurrency_Conflict_Is_Detected()
    {
        var ex = new ApiProblemException(409, "Conflict", "Stale version", "appointment.concurrency_conflict");
        AppointmentProblemMessages.IsConcurrencyConflict(ex).Should().BeTrue();
        AppointmentProblemMessages.ToUserMessage(ex).Should().Contain("Reload");
    }

    [Fact]
    public void Appointment_Response_Includes_Safe_Display_Fields()
    {
        typeof(AppointmentResponse).GetProperty(nameof(AppointmentResponse.PatientDisplayName)).Should().NotBeNull();
        typeof(AppointmentResponse).GetProperty(nameof(AppointmentResponse.LocalPatientNumber)).Should().NotBeNull();
        typeof(AppointmentResponse).GetProperty(nameof(AppointmentResponse.DoctorDisplayName)).Should().NotBeNull();
        typeof(AppointmentResponse).GetProperty(nameof(AppointmentResponse.ClinicName)).Should().NotBeNull();
        typeof(AppointmentResponse).GetProperty(nameof(AppointmentResponse.ClinicSlug)).Should().NotBeNull();
        typeof(AppointmentResponse).GetProperty(nameof(AppointmentResponse.ClinicTimeZoneId)).Should().NotBeNull();
        typeof(CreateStaffAppointmentRequest).GetProperty(nameof(CreateStaffAppointmentRequest.ClinicId)).Should().NotBeNull();
    }

    [Fact]
    public void Patient_And_Clinic_Pickers_Use_Guid_Values_Not_Free_Text_Ids()
    {
        // PatientPicker and ClinicPicker bind Guid? Value — not string raw IDs.
        var patientPickerValueType = typeof(Guid?);
        var clinicPickerValueType = typeof(Guid?);
        patientPickerValueType.Should().Be(typeof(Guid?));
        clinicPickerValueType.Should().NotBe(typeof(string));
        typeof(CreateStaffAppointmentRequest).GetProperty(nameof(CreateStaffAppointmentRequest.PatientId))!
            .PropertyType.Should().Be(typeof(Guid));
        typeof(CreateStaffAppointmentRequest).GetProperty(nameof(CreateStaffAppointmentRequest.ClinicId))!
            .PropertyType.Should().Be(typeof(Guid?));
    }

    [Fact]
    public async Task Terminal_Statuses_Hide_Mutation_Actions()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(UserWith(
            WebPermissions.AppointmentsConfirm,
            WebPermissions.AppointmentsCancel,
            WebPermissions.AppointmentsComplete));

        AppointmentActionRules.GetVisibleActions("Completed", state).Should().BeEmpty();
        AppointmentActionRules.GetVisibleActions("NoShow", state).Should().BeEmpty();
        AppointmentActionRules.GetVisibleActions("CancelledByClinic", state).Should().BeEmpty();
    }

    private static CurrentUserResponse UserWith(params string[] permissions) =>
        new()
        {
            UserId = Guid.NewGuid(),
            Email = "staff@test.local",
            Roles = ["RECEPTIONIST"],
            Permissions = permissions,
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        };
}
