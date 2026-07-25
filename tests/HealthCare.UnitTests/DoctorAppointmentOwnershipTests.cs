using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Staff;

namespace HealthCare.UnitTests;

public sealed class DoctorAppointmentOwnershipTests
{
    [Fact]
    public async Task Doctor_Cannot_Create_Staff_Appointments()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var act = () => sut.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AuthorizationException>()
            .Where(e => e.ErrorCode == AuthorizationErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Doctor_Lists_Only_Own_Appointments_Even_When_Peer_Filter_Sent()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var peer = await SeedPeerDoctorAsync(h, data);
        var clinicAdmin = h.CreateStaffService(Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.ClinicAdmin);

        var own = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });
        var peerAppt = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = peer.StaffId,
            AppointmentDateUtc = h.Now.AddDays(3),
            DurationMinutes = 30,
        });

        var doctor = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var list = await doctor.ListForStaffAsync(new AppointmentListQuery
        {
            DoctorStaffMemberId = peer.StaffId,
        });

        list.Items.Should().Contain(i => i.Id == own.Id);
        list.Items.Should().NotContain(i => i.Id == peerAppt.Id);
        list.Items.Should().OnlyContain(i => i.DoctorStaffMemberId == data.DoctorAStaffId);
    }

    [Fact]
    public async Task Doctor_Cannot_View_Or_Mutate_Same_Clinic_Peer_Appointment()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var peer = await SeedPeerDoctorAsync(h, data);
        var clinicAdmin = h.CreateStaffService(Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.ClinicAdmin);
        var peerAppt = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = peer.StaffId,
            AppointmentDateUtc = h.Now.AddDays(4),
            DurationMinutes = 30,
        });

        var doctor = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        await FluentActions.Awaiting(() => doctor.GetByIdAsync(peerAppt.Id))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);

        await FluentActions.Awaiting(() => doctor.ConfirmAsync(
                peerAppt.Id,
                new AppointmentActionRequest { ExpectedVersion = peerAppt.Version }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);

        await FluentActions.Awaiting(() => doctor.RescheduleAsync(
                peerAppt.Id,
                new RescheduleAppointmentRequest
                {
                    AppointmentDateUtc = h.Now.AddDays(5),
                    DurationMinutes = 30,
                    ExpectedVersion = peerAppt.Version,
                }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied);
    }

    [Fact]
    public async Task Clinic_Admin_Still_Sees_Clinic_Wide_Appointments()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var peer = await SeedPeerDoctorAsync(h, data);
        var clinicAdmin = h.CreateStaffService(Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.ClinicAdmin);

        var forDoctorA = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(6),
            DurationMinutes = 30,
        });
        var forPeer = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = peer.StaffId,
            AppointmentDateUtc = h.Now.AddDays(7),
            DurationMinutes = 30,
        });

        var list = await clinicAdmin.ListForStaffAsync(new AppointmentListQuery());
        list.Items.Should().Contain(i => i.Id == forDoctorA.Id);
        list.Items.Should().Contain(i => i.Id == forPeer.Id);
        list.Items.Should().OnlyContain(i => i.ClinicId == data.ClinicAId);

        var completed = await clinicAdmin.MarkNoShowAsync(
            forPeer.Id,
            new AppointmentActionRequest { ExpectedVersion = forPeer.Version });
        completed.Status.Should().Be(nameof(AppointmentStatus.NoShow));
    }

    private static async Task<(Guid UserId, Guid StaffId)> SeedPeerDoctorAsync(
        AppointmentHarness harness,
        AppointmentHarness.SeedData data)
    {
        var userId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        harness.Db.StaffMembers.Add(new StaffMember
        {
            Id = staffId,
            UserId = userId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.Doctor,
            IsActive = true,
        });

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            harness.Db.DoctorAvailabilities.Add(new Domain.Appointments.DoctorAvailability
            {
                Id = Guid.NewGuid(),
                OrganizationId = data.Org1Id,
                ClinicId = data.ClinicAId,
                DoctorStaffMemberId = staffId,
                DayOfWeek = day,
                StartLocalTime = new TimeOnly(8, 0),
                EndLocalTime = new TimeOnly(20, 0),
                SlotDurationMinutes = 30,
                EffectiveFrom = new DateOnly(2020, 1, 1),
                IsActive = true,
            });
        }

        await harness.Db.SaveChangesAsync();
        return (userId, staffId);
    }
}
