using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.MedicalNotes;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.MedicalNotes;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

/// <summary>
/// DR-9: table-driven cross-role permission and ownership negative matrix (service layer).
/// </summary>
public sealed class CrossRoleAuthorizationMatrixTests
{
    public static TheoryData<string, string> ForbiddenPermissionCases()
    {
        var data = new TheoryData<string, string>();

        void Deny(string role, params string[] permissions)
        {
            foreach (var permission in permissions)
            {
                data.Add(role, permission);
            }
        }

        Deny(
            AppRoles.Doctor,
            Permissions.Appointments.Create,
            Permissions.Clinics.ReportsRead,
            Permissions.Clinics.AuditLogsRead,
            Permissions.Clinics.DashboardRead,
            Permissions.Clinics.ProfileUpdate,
            Permissions.Organizations.DashboardRead,
            Permissions.Organizations.ReportsRead,
            Permissions.Organizations.AuditLogsRead,
            Permissions.Hangfire.Dashboard,
            Permissions.Staff.Manage,
            Permissions.Availability.ManageClinic);

        Deny(
            AppRoles.ClinicAdmin,
            Permissions.MedicalNotes.Read,
            Permissions.MedicalNotes.Create,
            Permissions.MedicalNotes.Sign,
            Permissions.MedicalNotes.Amend,
            Permissions.Organizations.DashboardRead,
            Permissions.Hangfire.Dashboard,
            Permissions.Doctors.DashboardRead);

        Deny(
            AppRoles.OrganizationAdmin,
            Permissions.MedicalNotes.Read,
            Permissions.MedicalNotes.Create,
            Permissions.Hangfire.Dashboard);

        Deny(
            AppRoles.Receptionist,
            Permissions.MedicalNotes.Read,
            Permissions.MedicalNotes.Create,
            Permissions.Appointments.Complete,
            Permissions.Appointments.NoShow,
            Permissions.Clinics.ReportsRead,
            Permissions.Clinics.AuditLogsRead);

        Deny(
            AppRoles.Nurse,
            Permissions.MedicalNotes.Amend,
            Permissions.Appointments.Create,
            Permissions.Appointments.Reschedule,
            Permissions.Availability.ManageSelf,
            Permissions.Clinics.ReportsRead);

        Deny(
            AppRoles.Patient,
            Permissions.Patients.Search,
            Permissions.MedicalNotes.Read,
            Permissions.Appointments.Complete,
            Permissions.Appointments.CheckIn,
            Permissions.Clinics.ReportsRead,
            Permissions.Staff.Read,
            Permissions.Reminders.Read);

        return data;
    }

    [Theory]
    [MemberData(nameof(ForbiddenPermissionCases))]
    public void Role_Is_Denied_Forbidden_Permission(string role, string permission)
    {
        RolePermissionMatrix.RoleHasPermission(role, permission).Should().BeFalse(
            because: $"{role} must not receive {permission}");

        var sut = CreatePermissionService(role);
        sut.HasPermission(permission).Should().BeFalse();
    }

    [Fact]
    public void Doctor_Retains_Clinical_Permissions_But_Not_Admin_Surfaces()
    {
        var sut = CreatePermissionService(AppRoles.Doctor);
        sut.HasPermission(Permissions.Appointments.Complete).Should().BeTrue();
        sut.HasPermission(Permissions.MedicalNotes.Amend).Should().BeTrue();
        sut.HasPermission(Permissions.Doctors.DashboardRead).Should().BeTrue();
        sut.HasPermission(Permissions.Clinics.ReportsRead).Should().BeFalse();
        sut.HasPermission(Permissions.Clinics.AuditLogsRead).Should().BeFalse();
    }

    [Fact]
    public async Task Peer_Doctor_Complete_With_Stale_Version_Returns_NotFound_Not_Conflict()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var peer = await SeedPeerDoctorAsync(h, data);
        var clinicAdmin = h.CreateStaffService(Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.ClinicAdmin);
        var peerAppt = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = peer.StaffId,
            AppointmentDateUtc = h.Now.AddDays(5),
            DurationMinutes = 30,
        });

        var doctor = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        await FluentActions.Awaiting(() => doctor.CompleteAsync(
                peerAppt.Id,
                new AppointmentActionRequest { ExpectedVersion = 9999 }))
            .Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotFoundOrDenied
                        && e.StatusCode == 404);

        var unchanged = await clinicAdmin.GetByIdAsync(peerAppt.Id);
        unchanged.Status.Should().Be(nameof(AppointmentStatus.Confirmed));
        unchanged.Version.Should().Be(peerAppt.Version);
    }

    [Fact]
    public async Task Peer_Doctor_Cannot_Create_Or_Read_Note_On_Peer_Appointment()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var peer = await SeedPeerDoctorAsync(h, data);
        var clinicAdmin = h.CreateStaffService(Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.ClinicAdmin);
        var peerAppt = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = peer.StaffId,
            AppointmentDateUtc = h.Now.AddDays(6),
            DurationMinutes = 30,
        });
        var checkedIn = await clinicAdmin.CheckInAsync(
            peerAppt.Id,
            new AppointmentActionRequest { ExpectedVersion = peerAppt.Version });

        var ownerNotes = h.CreateMedicalNoteService(
            peer.UserId, data.Org1Id, data.ClinicAId, peer.StaffId, AppRoles.Doctor);
        var note = await ownerNotes.CreateDraftAsync(checkedIn.Id, new CreateMedicalNoteDraftRequest
        {
            NoteType = "Progress",
            Plan = "Peer owned plan",
        });

        var peerDoctor = h.CreateMedicalNoteService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        await FluentActions.Awaiting(() => peerDoctor.CreateDraftAsync(
                checkedIn.Id,
                new CreateMedicalNoteDraftRequest { NoteType = "Progress", Plan = "Intrusion" }))
            .Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.NotFound);

        await FluentActions.Awaiting(() => peerDoctor.GetByIdAsync(note.Id))
            .Should().ThrowAsync<MedicalNoteException>()
            .Where(e => e.ErrorCode == MedicalNoteErrorCodes.NotFound);

        (await h.Db.MedicalNotes.CountAsync(n => n.AppointmentId == checkedIn.Id)).Should().Be(1);
        (await h.Db.MedicalNotes.AsNoTracking().SingleAsync(n => n.Id == note.Id)).Plan.Should().Be("Peer owned plan");
    }

    [Fact]
    public async Task Clinic_Staff_Cannot_Reach_Foreign_Organization_Patient_Directory()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.ClinicAdmin,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest());
        result.Items.Should().OnlyContain(i => i.ClinicId == data.ClinicAId);
        result.Items.Should().NotContain(i => i.PatientId == data.PatientInOtherOrgId);
        result.Items.Should().NotContain(i => i.ClinicId == data.OtherOrgClinicId);
        result.Items.Should().NotContain(i => i.PatientId == data.PatientInBId);
    }

    [Fact]
    public async Task Denied_Peer_Complete_Does_Not_Write_Succeeded_Audit()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var peer = await SeedPeerDoctorAsync(h, data);
        var clinicAdmin = h.CreateStaffService(Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.ClinicAdmin);
        var peerAppt = await clinicAdmin.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = peer.StaffId,
            AppointmentDateUtc = h.Now.AddDays(7),
            DurationMinutes = 30,
        });

        var audit = new RecordingClinicalWorkflowAuditLogger();
        var doctor = h.CreateStaffService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor, audit);

        await FluentActions.Awaiting(() => doctor.CompleteAsync(
                peerAppt.Id,
                new AppointmentActionRequest { ExpectedVersion = peerAppt.Version }))
            .Should().ThrowAsync<AppointmentException>();

        audit.Events.Should().NotContain(e =>
            e.Operation == "appointment_completed" && e.ResultCode == "succeeded");
    }

    private static IPermissionService CreatePermissionService(string role)
    {
        var isPatient = role == AppRoles.Patient;
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [role],
            PatientId = isPatient ? Guid.NewGuid() : null,
        };
        var staff = isPatient
            ? new FakeCurrentStaff()
            : new FakeCurrentStaff
            {
                HasActiveMembership = true,
                StaffMemberId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                ClinicId = Guid.NewGuid(),
                Role = role,
            };
        var patient = new FakeCurrentPatient
        {
            HasLinkedPatient = isPatient,
            PatientId = user.PatientId,
        };
        return new PermissionService(user, staff, patient, new NoOpAuthorizationAuditLogger());
    }

    private static async Task<(Guid UserId, Guid StaffId)> SeedPeerDoctorAsync(
        AppointmentHarness h,
        AppointmentHarness.SeedData data)
    {
        var userId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = staffId,
            UserId = userId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.Doctor,
            FirstName = "Peer",
            LastName = "Doctor",
            IsActive = true,
        });

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            h.Db.DoctorAvailabilities.Add(new Domain.Appointments.DoctorAvailability
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

        await h.Db.SaveChangesAsync();
        return (userId, staffId);
    }
}
