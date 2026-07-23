using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

public sealed class AppointmentRescheduleTests
{
    [Fact]
    public async Task Patient_Reschedules_Own_Requested_Appointment()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));

        var rescheduled = await sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
            Reason = "Conflict",
        });

        rescheduled.Id.Should().Be(created.Id);
        rescheduled.Status.Should().Be(nameof(AppointmentStatus.Requested));
        rescheduled.Version.Should().Be(created.Version + 1);
        rescheduled.AppointmentDateUtc.Should().Be(h.Now.AddDays(3));
    }

    [Fact]
    public async Task Patient_Reschedules_Own_Confirmed_Appointment()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var staff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var created = await CreateRequestedAsync(patient, data, h.Now.AddDays(2));
        var confirmed = await staff.ConfirmAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });

        var rescheduled = await patient.RescheduleAsync(confirmed.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(4),
            DurationMinutes = 30,
            ExpectedVersion = confirmed.Version,
        });

        rescheduled.Status.Should().Be(nameof(AppointmentStatus.Confirmed));
        rescheduled.Id.Should().Be(confirmed.Id);
    }

    [Fact]
    public async Task Patient_Cannot_Reschedule_Another_Patients_Appointment()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var owner = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(owner, data, h.Now.AddDays(2));

        var otherPatientId = await h.SeedExtraPatientInClinicAAsync(data);
        var otherUserId = Guid.NewGuid();
        var other = h.CreatePatientService(otherUserId, otherPatientId);

        var act = () => other.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);
    }

    [Fact]
    public async Task Staff_Can_Reschedule_In_Clinic()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var staff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var created = await staff.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var rescheduled = await staff.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(5),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        rescheduled.AppointmentDateUtc.Should().Be(h.Now.AddDays(5));
    }

    [Fact]
    public async Task Cross_Clinic_Staff_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var clinicAStaff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var created = await clinicAStaff.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var clinicBStaff = h.CreateStaffService(data.DoctorBUserId, data.Org1Id, data.ClinicBId, data.DoctorBStaffId, AppRoles.Doctor);
        var act = () => clinicBStaff.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);
    }

    [Fact]
    public async Task Cross_Organization_Access_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var clinicAStaff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var created = await clinicAStaff.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var org2 = Guid.NewGuid();
        var clinic2 = Guid.NewGuid();
        var staff2 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        h.Db.Organizations.Add(new Organization
        {
            Id = org2,
            Name = "Org2",
            Slug = "org-2",
            Status = OrganizationStatus.Active,
        });
        h.Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinic2,
            OrganizationId = org2,
            Name = "C",
            Slug = "clinic-c",
            TimeZoneId = "Asia/Riyadh",
            IsActive = true,
        });
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = staff2,
            UserId = user2,
            OrganizationId = org2,
            ClinicId = clinic2,
            Role = AppRoles.OrganizationAdmin,
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        var otherOrg = h.CreateStaffService(user2, org2, clinic2, staff2, AppRoles.OrganizationAdmin);
        var act = () => otherOrg.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);
    }

    [Fact]
    public async Task Terminal_Appointment_Cannot_Be_Rescheduled()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(patient, data, h.Now.AddDays(2));
        await patient.CancelAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });

        var act = () => patient.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version + 1,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.RescheduleNotAllowed);
    }

    [Fact]
    public async Task Checked_In_Appointment_Cannot_Be_Rescheduled()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var staff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var created = await staff.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });
        var checkedIn = await staff.CheckInAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });

        var act = () => staff.RescheduleAsync(checkedIn.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = checkedIn.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.RescheduleNotAllowed);
    }

    [Fact]
    public async Task Past_Slot_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));

        var act = () => sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddHours(-1),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidTime);
    }

    [Fact]
    public async Task Outside_Availability_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));

        // 03:00 UTC ≈ 06:00 Asia/Riyadh — before 08:00 local window
        var act = () => sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = DateTimeOffset.Parse("2026-07-30T03:00:00Z"),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.OutsideAvailability);
    }

    [Fact]
    public async Task Exception_Blocked_Slot_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        h.Db.DoctorAvailabilityExceptions.Add(new DoctorAvailabilityException
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            Date = new DateOnly(2026, 7, 30),
            ExceptionType = AvailabilityExceptionType.UnavailableRange,
            StartLocalTime = new TimeOnly(9, 0),
            EndLocalTime = new TimeOnly(10, 0),
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));

        var act = () => sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = DateTimeOffset.Parse("2026-07-30T06:30:00Z"),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.AvailabilityException);
    }

    [Fact]
    public async Task Overlap_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var first = await CreateRequestedAsync(sut, data, h.Now.AddDays(3));
        var second = await CreateRequestedAsync(sut, data, h.Now.AddDays(4));

        var act = () => sut.RescheduleAsync(second.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = first.AppointmentDateUtc,
            DurationMinutes = 30,
            ExpectedVersion = second.Version,
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.SlotUnavailable);
    }

    [Fact]
    public async Task Cancelled_Appointment_Does_Not_Block_New_Slot()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var blocker = await CreateRequestedAsync(sut, data, h.Now.AddDays(3));
        await sut.CancelAsync(blocker.Id, new AppointmentActionRequest { ExpectedVersion = blocker.Version });

        var other = await CreateRequestedAsync(sut, data, h.Now.AddDays(4));
        var rescheduled = await sut.RescheduleAsync(other.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = blocker.AppointmentDateUtc,
            DurationMinutes = 30,
            ExpectedVersion = other.Version,
        });

        rescheduled.AppointmentDateUtc.Should().Be(blocker.AppointmentDateUtc);
    }

    [Fact]
    public async Task Doctor_From_Another_Clinic_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));

        var act = () => sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidAssignedStaff);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_409()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));

        var act = () => sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version + 5,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.ConcurrencyConflict && e.StatusCode == 409);
    }

    [Fact]
    public async Task Existing_Appointment_Id_Is_Preserved()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));
        var id = created.Id;

        var rescheduled = await sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(6),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        rescheduled.Id.Should().Be(id);
        (await h.Db.Appointments.CountAsync(a => a.Id == id)).Should().Be(1);
    }

    [Fact]
    public async Task Old_Upcoming_Reminder_Is_Cancelled_And_New_Created_Once()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var start = h.Now.AddDays(5);
        var created = await CreateRequestedAsync(sut, data, start);

        var upcomingBefore = await h.Db.AppointmentReminders.SingleAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Upcoming);
        var oldSchedule = upcomingBefore.ScheduledAtUtc;
        var reminderId = upcomingBefore.Id;

        var newStart = h.Now.AddDays(8);
        await sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = newStart,
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        var upcomings = await h.Db.AppointmentReminders
            .Where(r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Upcoming)
            .ToListAsync();
        upcomings.Should().ContainSingle();
        upcomings[0].Id.Should().Be(reminderId);
        upcomings[0].Status.Should().Be(AppointmentReminderStatus.Pending);
        upcomings[0].ScheduledAtUtc.Should().Be(newStart - TimeSpan.FromHours(24));
        upcomings[0].ScheduledAtUtc.Should().NotBe(oldSchedule);
    }

    [Fact]
    public async Task Sent_Confirmation_Reminder_Is_Not_Duplicated()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(3));

        var confirmation = await h.Db.AppointmentReminders.SingleAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation);
        confirmation.Status = AppointmentReminderStatus.Sent;
        confirmation.SentAtUtc = h.Now;
        await h.Db.SaveChangesAsync();

        await sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(4),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        var confirmations = await h.Db.AppointmentReminders
            .Where(r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation)
            .ToListAsync();
        confirmations.Should().ContainSingle();
        confirmations[0].Status.Should().Be(AppointmentReminderStatus.Sent);
        confirmations[0].Id.Should().Be(confirmation.Id);
    }

    [Fact]
    public async Task Duplicate_Reschedule_Reminder_Call_Does_Not_Duplicate_Reminders()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(3));
        await sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(4),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await h.CreateReminderScheduler().ScheduleAfterAppointmentRescheduledAsync(created.Id);
        await h.CreateReminderScheduler().ScheduleAfterAppointmentRescheduledAsync(created.Id);

        var count = await h.Db.AppointmentReminders.CountAsync(r => r.AppointmentId == created.Id);
        count.Should().Be(2); // Confirmation + Upcoming
    }

    [Fact]
    public async Task Reschedule_History_Is_Recorded()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));
        var newStart = h.Now.AddDays(7);

        await sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = newStart,
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
            Reason = "Travel",
        });

        var history = await h.Db.AppointmentRescheduleHistories.SingleAsync(x => x.AppointmentId == created.Id);
        history.PreviousStartUtc.Should().Be(created.AppointmentDateUtc);
        history.NewStartUtc.Should().Be(newStart);
        history.PreviousDoctorStaffMemberId.Should().Be(data.DoctorAStaffId);
        history.NewDoctorStaffMemberId.Should().Be(data.DoctorAStaffId);
        history.PreviousVersion.Should().Be(created.Version);
        history.Reason.Should().Be("Travel");
        history.RescheduledByUserId.Should().Be(data.PatientUserId);
    }

    [Fact]
    public void Reschedule_Contract_Rejects_Tenant_And_Status_Fields()
    {
        typeof(RescheduleAppointmentRequest).GetProperty("PatientId").Should().BeNull();
        typeof(RescheduleAppointmentRequest).GetProperty("OrganizationId").Should().BeNull();
        typeof(RescheduleAppointmentRequest).GetProperty("ClinicId").Should().BeNull();
        typeof(RescheduleAppointmentRequest).GetProperty("Status").Should().BeNull();
        typeof(RescheduleAppointmentRequest).GetProperty("CreatedByUserId").Should().BeNull();
    }

    [Fact]
    public async Task Same_Slot_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateRequestedAsync(sut, data, h.Now.AddDays(2));

        var act = () => sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            DoctorStaffMemberId = created.DoctorStaffMemberId,
            AppointmentDateUtc = created.AppointmentDateUtc,
            DurationMinutes = created.DurationMinutes,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.RescheduleSameSlot);
    }

    private static Task<AppointmentResponse> CreateRequestedAsync(
        AppointmentService sut,
        AppointmentHarness.SeedData data,
        DateTimeOffset start) =>
        sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });
}
