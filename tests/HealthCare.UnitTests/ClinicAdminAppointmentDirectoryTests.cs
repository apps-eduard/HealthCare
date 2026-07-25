using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class ClinicAdminAppointmentDirectoryTests
{
    [Fact]
    public async Task Clinic_Admin_Lists_Only_Own_Clinic_Appointments()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var own = await h.SeedAppointmentAsync(data, AppointmentStatus.Confirmed);
        await h.EnrollPatientInClinicBAsync(data);
        var clinicBPatientId = await h.Db.ClinicPatients
            .Where(cp => cp.ClinicId == data.ClinicBId && cp.PatientId == data.PatientId)
            .Select(cp => cp.Id)
            .SingleAsync();
        var other = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicBId,
            PatientId = data.PatientId,
            ClinicPatientId = clinicBPatientId,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(1),
            DurationMinutes = 30,
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.Staff,
            CreatedByUserId = data.DoctorBUserId,
            Version = 0,
            CreatedAtUtc = h.Now,
            UpdatedAtUtc = h.Now,
        };
        h.Db.Appointments.Add(other);
        await h.Db.SaveChangesAsync();

        var sut = h.CreateStaffService(admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);
        var list = await sut.ListForStaffAsync(new AppointmentListQuery());
        list.Items.Should().Contain(i => i.Id == own.Id);
        list.Items.Should().OnlyContain(i => i.ClinicId == data.ClinicAId);
        list.Items.Should().NotContain(i => i.Id == other.Id);
    }

    [Fact]
    public async Task Clinic_Admin_Can_Complete_And_No_Show_From_Valid_Statuses()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateStaffService(admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        var checkedIn = await h.SeedAppointmentAsync(data, AppointmentStatus.CheckedIn);
        var completed = await sut.CompleteAsync(
            checkedIn.Id,
            new AppointmentActionRequest { ExpectedVersion = checkedIn.Version });
        completed.Status.Should().Be(nameof(AppointmentStatus.Completed));

        var notes = await h.Db.MedicalNotes.CountAsync(n => n.AppointmentId == checkedIn.Id);
        notes.Should().Be(0);

        var confirmed = await h.SeedAppointmentAsync(data, AppointmentStatus.Confirmed);
        var noShow = await sut.MarkNoShowAsync(
            confirmed.Id,
            new AppointmentActionRequest { ExpectedVersion = confirmed.Version });
        noShow.Status.Should().Be(nameof(AppointmentStatus.NoShow));
    }

    [Fact]
    public async Task Clinic_Admin_Invalid_Complete_From_Confirmed_Is_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateStaffService(admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);
        var confirmed = await h.SeedAppointmentAsync(data, AppointmentStatus.Confirmed);

        var act = () => sut.CompleteAsync(
            confirmed.Id,
            new AppointmentActionRequest { ExpectedVersion = confirmed.Version });
        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidTransition);
    }

    [Fact]
    public async Task Clinic_Admin_Cross_Clinic_Mutation_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        await h.EnrollPatientInClinicBAsync(data);
        var clinicBPatientId = await h.Db.ClinicPatients
            .Where(cp => cp.ClinicId == data.ClinicBId && cp.PatientId == data.PatientId)
            .Select(cp => cp.Id)
            .SingleAsync();
        var foreign = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicBId,
            PatientId = data.PatientId,
            ClinicPatientId = clinicBPatientId,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.Staff,
            CreatedByUserId = data.DoctorBUserId,
            Version = 0,
            CreatedAtUtc = h.Now,
            UpdatedAtUtc = h.Now,
        };
        h.Db.Appointments.Add(foreign);
        await h.Db.SaveChangesAsync();

        var sut = h.CreateStaffService(admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);
        var act = () => sut.MarkNoShowAsync(
            foreign.Id,
            new AppointmentActionRequest { ExpectedVersion = 0 });
        await act.Should().ThrowAsync<AppointmentException>();
    }

    [Fact]
    public async Task Inactive_Membership_Denied_For_Clinic_Admin_Queue()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = new AppointmentService(
            h.Db,
            new FakeCurrentUser
            {
                IsAuthenticated = true,
                UserId = admin.UserId,
                Roles = [AppRoles.ClinicAdmin],
            },
            new FakeCurrentStaff { HasActiveMembership = false },
            new FakeCurrentPatient(),
            new ClinicPublicLookup(h.Db),
            h.CreateSlots(),
            h.CreateReminderScheduler(),
            new NoOpAuthorizationAuditLogger(),
            h.Time,
            NullLogger<AppointmentService>.Instance);

        var act = () => sut.ListQueueForStaffAsync(new AppointmentQueueQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Stale_Version_Conflict_For_Clinic_Admin_No_Show()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateStaffService(admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);
        var confirmed = await h.SeedAppointmentAsync(data, AppointmentStatus.Confirmed);

        var act = () => sut.MarkNoShowAsync(
            confirmed.Id,
            new AppointmentActionRequest { ExpectedVersion = 99 });
        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Status_Update_Audit_Uses_Safe_Operation_Name()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var audit = new RecordingAppointmentAuditLogger();
        var sut = new AppointmentService(
            h.Db,
            new FakeCurrentUser
            {
                IsAuthenticated = true,
                UserId = admin.UserId,
                Roles = [AppRoles.ClinicAdmin],
            },
            new FakeCurrentStaff
            {
                HasActiveMembership = true,
                StaffMemberId = admin.StaffId,
                OrganizationId = data.Org1Id,
                ClinicId = data.ClinicAId,
                Role = AppRoles.ClinicAdmin,
            },
            new FakeCurrentPatient(),
            new ClinicPublicLookup(h.Db),
            h.CreateSlots(),
            h.CreateReminderScheduler(),
            audit,
            h.Time,
            NullLogger<AppointmentService>.Instance);

        var confirmed = await h.SeedAppointmentAsync(data, AppointmentStatus.Confirmed);
        await sut.MarkNoShowAsync(confirmed.Id, new AppointmentActionRequest { ExpectedVersion = 0 });

        audit.Operations.Should().Contain(o =>
            o.Operation == "appointment_no_show" && o.ResultCode == "succeeded");
        var json = System.Text.Json.JsonSerializer.Serialize(audit.Operations);
        json.ToLowerInvariant().Should().NotContain("password");
        json.ToLowerInvariant().Should().NotContain("token");
        json.ToLowerInvariant().Should().NotContain("medical");
    }

    [Fact]
    public void Clinic_Admin_Does_Not_Receive_Medical_Note_Permissions()
    {
        var permissions = RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin);
        permissions.Should().Contain(Permissions.Appointments.Complete);
        permissions.Should().NotContain(Permissions.MedicalNotes.Read);
        permissions.Should().NotContain(Permissions.MedicalNotes.Create);
    }

    private static async Task<(Guid UserId, Guid StaffId)> SeedClinicAdminAsync(
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
            Role = AppRoles.ClinicAdmin,
            IsActive = true,
        });
        await harness.Db.SaveChangesAsync();
        return (userId, staffId);
    }
}

internal sealed class RecordingAppointmentAuditLogger : NoOpAuthorizationAuditLogger
{
    public List<(string Operation, string ResultCode, Guid? OrganizationId, Guid? ClinicId, Guid? AppointmentId)> Operations { get; } = [];

    public override void AppointmentOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? appointmentId = null)
    {
        Operations.Add((operation, resultCode, organizationId, clinicId, appointmentId));
    }
}
