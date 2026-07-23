using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class AppointmentReminderTests
{
    [Fact]
    public async Task Confirmation_Reminder_Scheduled_Once()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var confirmations = await h.Db.AppointmentReminders
            .Where(r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation)
            .ToListAsync();
        confirmations.Should().ContainSingle();

        var staff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        await staff.ConfirmAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });

        confirmations = await h.Db.AppointmentReminders
            .Where(r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation)
            .ToListAsync();
        confirmations.Should().ContainSingle();
    }

    [Fact]
    public async Task Upcoming_Reminder_Scheduled_At_Correct_Utc_Time()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var start = h.Now.AddDays(3);
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });

        var upcoming = await h.Db.AppointmentReminders.SingleAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Upcoming);
        upcoming.ScheduledAtUtc.Should().Be(start - TimeSpan.FromHours(24));
    }

    [Fact]
    public void Clinic_Timezone_Formatting()
    {
        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var utc = DateTimeOffset.Parse("2026-07-30T06:30:00Z");
        var local = converter.ToClinicLocal(utc, "Asia/Riyadh");
        local.ToString("yyyy-MM-dd HH:mm").Should().Be("2026-07-30 09:30");
    }

    [Fact]
    public async Task Cancelled_Appointment_Cancels_Pending_Reminders()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        await sut.CancelAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });

        var rows = await h.Db.AppointmentReminders.Where(r => r.AppointmentId == created.Id).ToListAsync();
        rows.Where(r => r.ReminderType != AppointmentReminderType.Cancellation)
            .Should().OnlyContain(r => r.Status == AppointmentReminderStatus.Cancelled);
        rows.Should().Contain(r => r.ReminderType == AppointmentReminderType.Cancellation);
    }

    [Fact]
    public async Task Completed_Or_NoShow_Does_Not_Send_Reminder()
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

        var reminder = await h.Db.AppointmentReminders.FirstAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation);

        await staff.CheckInAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });
        var checkedIn = await h.Db.Appointments.SingleAsync(a => a.Id == created.Id);
        await staff.CompleteAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = checkedIn.Version });

        await h.CreateReminderProcessor().ProcessReminderAsync(created.Id, reminder.Id);
        reminder = await h.Db.AppointmentReminders.SingleAsync(r => r.Id == reminder.Id);
        reminder.Status.Should().Be(AppointmentReminderStatus.Cancelled);
    }

    [Fact]
    public async Task Duplicate_Scheduling_Prevented()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        await h.CreateReminderScheduler().ScheduleAfterAppointmentCreatedAsync(created.Id);
        var count = await h.Db.AppointmentReminders.CountAsync(r => r.AppointmentId == created.Id);
        count.Should().Be(2); // Confirmation + Upcoming
    }

    [Fact]
    public async Task Sent_Reminder_Is_Not_Resent()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var reminder = await h.Db.AppointmentReminders.FirstAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation);
        var processor = h.CreateReminderProcessor();
        await processor.ProcessReminderAsync(created.Id, reminder.Id);
        await processor.ProcessReminderAsync(created.Id, reminder.Id);

        reminder = await h.Db.AppointmentReminders.SingleAsync(r => r.Id == reminder.Id);
        reminder.Status.Should().Be(AppointmentReminderStatus.Sent);
        reminder.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Failed_Reminder_Retry_And_Max_Attempts()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var reminder = await h.Db.AppointmentReminders.FirstAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation);
        var failing = new FailingSender();
        var processor = h.CreateReminderProcessor(failing);

        for (var i = 0; i < AppointmentReminder.MaxAttempts; i++)
        {
            try
            {
                await processor.ProcessReminderAsync(created.Id, reminder.Id);
            }
            catch
            {
                // expected until permanent failure
            }
        }

        reminder = await h.Db.AppointmentReminders.SingleAsync(r => r.Id == reminder.Id);
        reminder.Status.Should().Be(AppointmentReminderStatus.Failed);
        reminder.AttemptCount.Should().Be(AppointmentReminder.MaxAttempts);

        var staffReminders = h.CreateReminderService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var retried = await staffReminders.RetryAsync(created.Id, reminder.Id);
        retried.Status.Should().Be(nameof(AppointmentReminderStatus.Pending));
    }

    [Fact]
    public async Task Recovery_Job_Requeues_Overdue_Pending_And_Ignores_Sent()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var confirmation = await h.Db.AppointmentReminders.FirstAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Confirmation);
        await h.CreateReminderProcessor().ProcessReminderAsync(created.Id, confirmation.Id);

        var upcoming = await h.Db.AppointmentReminders.SingleAsync(
            r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Upcoming);
        upcoming.ScheduledAtUtc = h.Now.AddMinutes(-5);
        await h.Db.SaveChangesAsync();

        h.Jobs.Enqueued.Clear();
        await h.CreateRecovery().RecoverOverdueAsync();

        h.Jobs.Enqueued.Should().Contain(e => e.ReminderId == upcoming.Id);
        h.Jobs.Enqueued.Should().NotContain(e => e.ReminderId == confirmation.Id);
    }

    [Fact]
    public async Task Cross_Clinic_Reminder_Access_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var clinicB = h.CreateReminderService(
            data.DoctorBUserId, data.Org1Id, data.ClinicBId, data.DoctorBStaffId, AppRoles.Doctor);
        var act = () => clinicB.ListForAppointmentAsync(created.Id);
        await act.Should().ThrowAsync<AppointmentException>();
    }

    [Fact]
    public async Task Patient_Role_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var patientSvc = h.CreateReminderService(
            data.PatientUserId, data.Org1Id, data.ClinicAId, Guid.Empty, AppRoles.Patient, isPatient: true);
        var act = () => patientSvc.ListForAppointmentAsync(created.Id);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Explicit_Platform_Admin_Bypass()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        var admin = new AppointmentReminderService(
            h.Db,
            new FakeCurrentUser { IsAuthenticated = true, UserId = Guid.NewGuid(), Roles = [AppRoles.PlatformAdmin] },
            new FakeCurrentStaff(),
            h.Jobs,
            h.Time,
            NullLogger<AppointmentReminderService>.Instance);

        var without = () => admin.ListForAppointmentAsync(created.Id);
        await without.Should().ThrowAsync<AuthorizationException>();

        var list = await admin.ListForAppointmentAsync(created.Id, PlatformAdminBypass.Explicit);
        list.Should().NotBeEmpty();
    }

    [Fact]
    public void Job_Arguments_Are_Ids_Only()
    {
        var method = typeof(AppointmentReminderHangfireJobs).GetMethod(
            nameof(AppointmentReminderHangfireJobs.ProcessReminderAsync));
        method.Should().NotBeNull();
        method!.GetParameters().Select(p => p.ParameterType)
            .Should().Equal(typeof(Guid), typeof(Guid), typeof(CancellationToken));
    }

    private sealed class FailingSender : IAppointmentReminderSender
    {
        public Task SendAsync(AppointmentReminderDeliveryRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated_delivery_failure");
    }
}
