using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

public sealed class AppointmentClinicalWorkflowTests
{
    [Fact]
    public void Transition_Matrix_Blocks_Backward_Skipped_And_Terminal_Outbound()
    {
        AppointmentStatusTransitions.CanComplete(AppointmentStatus.CheckedIn).Should().BeTrue();
        AppointmentStatusTransitions.CanComplete(AppointmentStatus.Confirmed).Should().BeFalse();

        AppointmentStatusTransitions.CanTransition(AppointmentStatus.Requested, AppointmentStatus.Completed)
            .Should().BeFalse();
        AppointmentStatusTransitions.CanTransition(AppointmentStatus.Confirmed, AppointmentStatus.Completed)
            .Should().BeFalse();
        AppointmentStatusTransitions.CanTransition(AppointmentStatus.Completed, AppointmentStatus.CheckedIn)
            .Should().BeFalse();
        AppointmentStatusTransitions.CanTransition(AppointmentStatus.CancelledByClinic, AppointmentStatus.Confirmed)
            .Should().BeFalse();
        AppointmentStatusTransitions.CanTransition(AppointmentStatus.NoShow, AppointmentStatus.Completed)
            .Should().BeFalse();

        AppointmentStatusTransitions.AllowedTargets(AppointmentStatus.Completed).Should().BeEmpty();
        AppointmentStatusTransitions.AllowedTargets(AppointmentStatus.CheckedIn)
            .Should().BeEquivalentTo(
            [
                AppointmentStatus.InProgress,
                AppointmentStatus.Completed,
                AppointmentStatus.NoShow,
                AppointmentStatus.CancelledByClinic,
            ]);
    }

    [Fact]
    public async Task Complete_Does_Not_Require_Medical_Note()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var staff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var created = await CreateConfirmedCheckedInAsync(h, data, staff);
        (await h.Db.MedicalNotes.CountAsync(n => n.AppointmentId == created.Id)).Should().Be(0);

        var completed = await staff.CompleteAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = created.Version });

        completed.Status.Should().Be(nameof(AppointmentStatus.Completed));
        (await h.Db.MedicalNotes.CountAsync(n => n.AppointmentId == created.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Repeated_Complete_Is_Safe_Without_Duplicate_Success_Audit()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var audit = new RecordingClinicalWorkflowAuditLogger();
        var staff = h.CreateStaffService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor, audit);

        var checkedIn = await CreateConfirmedCheckedInAsync(h, data, staff);
        var completed = await staff.CompleteAsync(
            checkedIn.Id,
            new AppointmentActionRequest { ExpectedVersion = checkedIn.Version });

        await FluentActions.Awaiting(() => staff.CompleteAsync(
                completed.Id,
                new AppointmentActionRequest { ExpectedVersion = completed.Version }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidTransition);

        await FluentActions.Awaiting(() => staff.CompleteAsync(
                completed.Id,
                new AppointmentActionRequest { ExpectedVersion = checkedIn.Version }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.ConcurrencyConflict);

        audit.Events.Count(e => e.Operation == "appointment_completed" && e.ResultCode == "succeeded")
            .Should().Be(1);
        audit.Events.Should().Contain(e =>
            e.Operation == "appointment_completed" && e.ResultCode == "invalid_transition");
        audit.Events.Should().Contain(e => e.ResultCode == "concurrency_conflict");
    }

    [Fact]
    public async Task Terminal_Appointment_Cannot_Be_Cancelled_Or_Rescheduled()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var staff = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var checkedIn = await CreateConfirmedCheckedInAsync(h, data, staff);
        var completed = await staff.CompleteAsync(
            checkedIn.Id,
            new AppointmentActionRequest { ExpectedVersion = checkedIn.Version });

        await FluentActions.Awaiting(() => staff.CancelAsync(
                completed.Id,
                new AppointmentActionRequest { ExpectedVersion = completed.Version }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidTransition);

        await FluentActions.Awaiting(() => staff.RescheduleAsync(
                completed.Id,
                new RescheduleAppointmentRequest
                {
                    ExpectedVersion = completed.Version,
                    AppointmentDateUtc = h.Now.AddDays(40),
                    DurationMinutes = 30,
                }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.RescheduleNotAllowed);
    }

    [Fact]
    public async Task Peer_Doctor_Complete_Returns_Not_Found()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var owner = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var peer = h.CreateStaffService(data.DoctorBUserId, data.Org1Id, data.ClinicAId, data.DoctorBStaffId, AppRoles.Doctor);

        var checkedIn = await CreateConfirmedCheckedInAsync(h, data, owner);
        await FluentActions.Awaiting(() => peer.CompleteAsync(
                checkedIn.Id,
                new AppointmentActionRequest { ExpectedVersion = checkedIn.Version }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);
    }

    [Fact]
    public async Task Successful_Complete_Writes_Succeeded_Audit()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var audit = new RecordingClinicalWorkflowAuditLogger();
        var staff = h.CreateStaffService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor, audit);

        var checkedIn = await CreateConfirmedCheckedInAsync(h, data, staff);
        await staff.CompleteAsync(checkedIn.Id, new AppointmentActionRequest { ExpectedVersion = checkedIn.Version });

        audit.Events.Should().ContainSingle(e =>
            e.Operation == "appointment_completed"
            && e.ResultCode == "succeeded"
            && e.AppointmentId == checkedIn.Id);
    }

    private static async Task<AppointmentResponse> CreateConfirmedCheckedInAsync(
        AppointmentHarness h,
        AppointmentHarness.SeedData data,
        AppointmentService staff)
    {
        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await patient.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(35),
            DurationMinutes = 30,
        });
        var confirmed = await staff.ConfirmAsync(
            created.Id,
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        return await staff.CheckInAsync(
            confirmed.Id,
            new AppointmentActionRequest { ExpectedVersion = confirmed.Version });
    }
}

internal sealed class RecordingClinicalWorkflowAuditLogger : NoOpAuthorizationAuditLogger
{
    public List<(string Operation, string ResultCode, Guid? AppointmentId)> Events { get; } = [];

    public override void AppointmentOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? appointmentId = null) =>
        Events.Add((operation, resultCode, appointmentId));
}
