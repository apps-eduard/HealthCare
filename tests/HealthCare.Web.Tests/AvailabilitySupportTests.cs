using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Availability;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class AvailabilitySupportTests
{
    [Fact]
    public async Task Missing_Manage_Permission_Blocks_Workspace()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "nurse@test.local",
            Roles = ["NURSE"],
            Permissions = [WebPermissions.AvailabilityRead, WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        AvailabilityPermissionRules.CanManage(state).Should().BeFalse();
        AvailabilityPermissionRules.CanView(state).Should().BeTrue();
        AvailabilityPermissionRules.IsSelfOnly(state).Should().BeFalse();
    }

    [Fact]
    public async Task Patient_Is_Denied_Staff_Availability_Context()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "p@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [WebPermissions.AvailabilityRead, WebPermissions.AvailabilityManageSelf],
            HasActiveStaffMembership = false,
        });

        state.IsPatientOnly.Should().BeTrue();
        AvailabilityPermissionRules.CanManage(state).Should().BeTrue(); // permission bit present
        // Page still blocks via IsPatientOnly before CanManage.
    }

    [Fact]
    public async Task Doctor_Self_Management_Is_Detectable()
    {
        var staffId = Guid.NewGuid();
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doc@test.local",
            Roles = ["DOCTOR"],
            Permissions = [WebPermissions.AvailabilityManageSelf, WebPermissions.AvailabilityRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            StaffMemberId = staffId,
        });

        AvailabilityPermissionRules.CanManage(state).Should().BeTrue();
        AvailabilityPermissionRules.IsSelfOnly(state).Should().BeTrue();
        state.CurrentUser!.StaffMemberId.Should().Be(staffId);
        state.CanFilterByClinic.Should().BeFalse();
    }

    [Fact]
    public async Task Clinic_Admin_Is_Not_Self_Only_And_Clinic_Is_Fixed()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.AvailabilityManageClinic,
                WebPermissions.AvailabilityRead,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        AvailabilityPermissionRules.IsSelfOnly(state).Should().BeFalse();
        AvailabilityPermissionRules.CanManage(state).Should().BeTrue();
        state.CanFilterByClinic.Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Can_Select_Authorized_Clinic()
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

        AvailabilityPermissionRules.CanManage(state).Should().BeTrue();
        state.CanFilterByClinic.Should().BeTrue();
    }

    [Fact]
    public void Availability_List_Groups_Monday_Through_Sunday()
    {
        var monday = Window("Monday", "09:00", "12:00");
        var sunday = Window("Sunday", "10:00", "11:00");
        var friday = Window("Friday", "14:00", "16:00");
        var fridayEarly = Window("Friday", "08:00", "09:00");

        var grouped = AvailabilityPresentation.GroupByDay([sunday, friday, monday, fridayEarly]);
        grouped.Keys.Should().Equal("Monday", "Friday", "Sunday");
        grouped["Friday"].Select(w => w.StartLocalTime).Should().Equal("08:00", "14:00");
    }

    [Fact]
    public void Create_Request_Contains_Local_Time_Fields()
    {
        var request = new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = "Tuesday",
            StartLocalTime = "09:30",
            EndLocalTime = "12:00",
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2026, 7, 1),
            EffectiveTo = new DateOnly(2026, 12, 31),
        };

        request.DayOfWeek.Should().Be("Tuesday");
        request.StartLocalTime.Should().Be("09:30");
        request.EndLocalTime.Should().Be("12:00");
        request.SlotDurationMinutes.Should().Be(30);
    }

    [Theory]
    [InlineData("12:00", "12:00", false)]
    [InlineData("13:00", "12:00", false)]
    [InlineData("09:00", "12:00", true)]
    [InlineData("bad", "12:00", false)]
    public void Invalid_Time_Range_Rejected_Client_Side(string start, string end, bool expected)
    {
        AvailabilityPresentation.IsValidWindow(start, end).Should().Be(expected);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(240, true)]
    [InlineData(241, false)]
    public void Slot_Duration_Validation(int minutes, bool expected) =>
        AvailabilityPresentation.IsValidDuration(minutes).Should().Be(expected);

    [Fact]
    public void Update_Sends_ExpectedVersion()
    {
        var request = new UpdateDoctorAvailabilityRequest
        {
            ExpectedVersion = 5,
            StartLocalTime = "10:00",
            EndLocalTime = "13:00",
            SlotDurationMinutes = 20,
            IsActive = true,
        };

        request.ExpectedVersion.Should().Be(5);
        request.StartLocalTime.Should().Be("10:00");
    }

    [Fact]
    public void Concurrency_Conflict_Maps_To_Reload_Prompt_Message()
    {
        var ex = new ApiProblemException(
            409,
            "Conflict",
            "stale",
            AvailabilityErrorCodes.AvailabilityConcurrency);
        AvailabilityProblemMessages.IsConcurrencyConflict(ex).Should().BeTrue();
        AvailabilityProblemMessages.ToUserMessage(ex)
            .Should().Contain("Reload");
    }

    [Theory]
    [InlineData("UnavailableFullDay", false)]
    [InlineData("UnavailableRange", true)]
    [InlineData("CustomAvailableRange", true)]
    public void Exception_Type_Controls_Required_Fields(string type, bool requiresTimes) =>
        AvailabilityPresentation.ExceptionRequiresTimes(type).Should().Be(requiresTimes);

    [Fact]
    public void Full_Day_Exception_Omits_Time_Fields()
    {
        var request = new CreateDoctorAvailabilityExceptionRequest
        {
            Date = new DateOnly(2026, 8, 1),
            ExceptionType = "UnavailableFullDay",
            StartLocalTime = null,
            EndLocalTime = null,
            Reason = "Holiday",
        };

        AvailabilityPresentation.ExceptionRequiresTimes(request.ExceptionType).Should().BeFalse();
        request.StartLocalTime.Should().BeNull();
        request.EndLocalTime.Should().BeNull();
    }

    [Fact]
    public void Range_Exception_Sends_Times()
    {
        var request = new CreateDoctorAvailabilityExceptionRequest
        {
            Date = new DateOnly(2026, 8, 2),
            ExceptionType = "UnavailableRange",
            StartLocalTime = "11:00",
            EndLocalTime = "13:00",
        };

        AvailabilityPresentation.ExceptionRequiresTimes(request.ExceptionType).Should().BeTrue();
        AvailabilityPresentation.IsValidWindow(request.StartLocalTime, request.EndLocalTime).Should().BeTrue();
    }

    [Fact]
    public void Slot_Preview_Contract_Uses_Clinic_Local_Labels()
    {
        var slot = new AvailableSlotResponse
        {
            StartUtc = DateTimeOffset.Parse("2026-07-24T06:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-07-24T06:30:00Z"),
            StartLocal = "09:00",
            EndLocal = "09:30",
            DurationMinutes = 30,
            TimeZoneId = "Asia/Riyadh",
        };

        AvailabilityPresentation.FormatLocalWindow(slot.StartLocal, slot.EndLocal).Should().Be("09:00 – 09:30");
        slot.TimeZoneId.Should().Be("Asia/Riyadh");
    }

    [Fact]
    public void Api_Errors_Map_To_Safe_Messages()
    {
        var overlap = new ApiProblemException(409, "Conflict", "raw stack", AvailabilityErrorCodes.AvailabilityConflict);
        AvailabilityProblemMessages.ToUserMessage(overlap)
            .Should().Be("This availability window overlaps an existing window.");
        AvailabilityProblemMessages.ToUserMessage(overlap).Should().NotContain("raw");
    }

    [Fact]
    public void Exception_And_Active_Labels_Are_Centralized()
    {
        AvailabilityPresentation.ExceptionTypeLabel("UnavailableFullDay").Should().Contain("full day");
        AvailabilityPresentation.ActiveLabel(true).Should().Be("Active");
        AvailabilityPresentation.FormatEffectiveRange(new DateOnly(2026, 1, 1), null)
            .Should().Contain("open");
        AvailabilityPresentation.DaysOfWeekOrdered.Should().HaveCount(7);
        AvailabilityPresentation.DaysOfWeekOrdered[0].Should().Be("Monday");
    }

    [Fact]
    public void Typed_Availability_Client_Surface_Exists()
    {
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.ListClinicDoctorsAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.ListClinicDoctorsByIdAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.ListAvailabilityAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.CreateAvailabilityAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.UpdateAvailabilityAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.DeleteAvailabilityAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.ListExceptionsAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.CreateExceptionAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.DeleteExceptionAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.GetAvailableSlotsAsync))
            .Should().NotBeNull();
    }

    private static DoctorAvailabilityResponse Window(string day, string start, string end) =>
        new()
        {
            Id = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            DoctorStaffMemberId = Guid.NewGuid(),
            DayOfWeek = day,
            StartLocalTime = start,
            EndLocalTime = end,
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true,
            Version = 1,
        };
}
