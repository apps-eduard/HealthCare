using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

/// <summary>PM-1: two-hour patient cancel/reschedule cutoff + authz-before-conflict.</summary>
public sealed class PatientScheduleMutationCutoffTests
{
    [Fact]
    public async Task Patient_Cancel_Allowed_Exactly_Two_Hours_Before_Start()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(sut, data, h.Now.AddDays(1));
        await SetStartAsync(h, created.Id, h.Now.Add(AppointmentService.PatientScheduleMutationCutoff));

        var cancelled = await sut.CancelAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = created.Version });

        cancelled.Status.Should().Be(nameof(AppointmentStatus.CancelledByPatient));
    }

    [Fact]
    public async Task Patient_Cancel_Denied_Inside_Two_Hour_Window()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(sut, data, h.Now.AddDays(1));
        await SetStartAsync(h, created.Id, h.Now.AddHours(2).AddMinutes(-1));

        var act = () => sut.CancelAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = created.Version });

        var ex = await act.Should().ThrowAsync<AppointmentException>();
        ex.Which.ErrorCode.Should().Be(AppointmentErrorCodes.PatientMutationCutoff);
        ex.Which.StatusCode.Should().Be(409);

        (await h.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == created.Id))
            .Status.Should().Be(AppointmentStatus.Requested);
    }

    [Fact]
    public async Task Patient_Cancel_Denied_When_Appointment_Already_Started()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(sut, data, h.Now.AddDays(1));
        await SetStartAsync(h, created.Id, h.Now.AddHours(-1));

        var act = () => sut.CancelAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = created.Version });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.PatientMutationCutoff);
    }

    [Fact]
    public async Task Staff_Cancel_Inside_Two_Hour_Window_Still_Allowed()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(patient, data, h.Now.AddDays(1));
        await SetStartAsync(h, created.Id, h.Now.AddMinutes(30));

        var staff = h.CreateStaffService(
            Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.ClinicAdmin);

        var cancelled = await staff.CancelAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = created.Version });

        cancelled.Status.Should().Be(nameof(AppointmentStatus.CancelledByClinic));
    }

    [Fact]
    public async Task Patient_Reschedule_Allowed_Exactly_Two_Hours_Before_Start()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(sut, data, h.Now.AddDays(1));
        await SetStartAsync(h, created.Id, h.Now.Add(AppointmentService.PatientScheduleMutationCutoff));

        var rescheduled = await sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        rescheduled.AppointmentDateUtc.Should().Be(h.Now.AddDays(2));
    }

    [Fact]
    public async Task Patient_Reschedule_Denied_Inside_Two_Hour_Window()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(sut, data, h.Now.AddDays(1));
        await SetStartAsync(h, created.Id, h.Now.AddHours(1));

        var act = () => sut.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
            ExpectedVersion = created.Version,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.PatientMutationCutoff);

        (await h.Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == created.Id))
            .AppointmentDateUtc.Should().Be(h.Now.AddHours(1));
    }

    [Fact]
    public async Task Foreign_Appointment_Cancel_With_Stale_Version_Returns_NotFound()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var owner = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(owner, data, h.Now.AddDays(2));

        var otherPatient = await h.SeedSecondPatientAsync(data);
        var other = h.CreatePatientService(otherPatient.UserId, otherPatient.PatientId);

        var act = () => other.CancelAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = 0 });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied
                        && e.StatusCode == 404);
    }

    [Fact]
    public async Task Foreign_Appointment_Cancel_Inside_Cutoff_Still_Returns_NotFound()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var owner = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(owner, data, h.Now.AddDays(2));
        await SetStartAsync(h, created.Id, h.Now.AddMinutes(30));

        var otherPatient = await h.SeedSecondPatientAsync(data);
        var other = h.CreatePatientService(otherPatient.UserId, otherPatient.PatientId);

        var act = () => other.CancelAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = created.Version });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);
    }

    [Fact]
    public async Task Foreign_Appointment_Reschedule_With_Conflicting_Slot_Returns_NotFound()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var owner = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await CreateAsync(owner, data, h.Now.AddDays(2));
        var blockerStart = h.Now.AddDays(5);
        await CreateAsync(owner, data, blockerStart);

        var otherPatient = await h.SeedSecondPatientAsync(data);
        var other = h.CreatePatientService(otherPatient.UserId, otherPatient.PatientId);

        var act = () => other.RescheduleAsync(created.Id, new RescheduleAppointmentRequest
        {
            AppointmentDateUtc = blockerStart,
            DurationMinutes = 30,
            ExpectedVersion = 0,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);
    }

    [Fact]
    public async Task Patient_List_Omits_Staff_Only_Display_Fields()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        await CreateAsync(sut, data, h.Now.AddDays(2));

        var page = await sut.ListForCurrentPatientAsync(new AppointmentListQuery());
        var item = page.Items.Should().ContainSingle().Subject;
        item.PatientDisplayName.Should().BeNull();
        item.LocalPatientNumber.Should().BeNull();
        item.ClinicName.Should().NotBeNullOrWhiteSpace();
        item.ClinicId.Should().NotBe(Guid.Empty);
    }

    private static async Task<AppointmentResponse> CreateAsync(
        IAppointmentService sut,
        AppointmentHarness.SeedData data,
        DateTimeOffset start) =>
        await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
            Reason = "PM-1",
        });

    private static async Task SetStartAsync(AppointmentHarness h, Guid appointmentId, DateTimeOffset start)
    {
        var appt = await h.Db.Appointments.SingleAsync(a => a.Id == appointmentId);
        appt.AppointmentDateUtc = start;
        await h.Db.SaveChangesAsync();
    }
}
