using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

public sealed class DoctorPatientAccessTests
{
    [Fact]
    public async Task Doctor_Without_Assigned_Appointments_Sees_Empty_Directory()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest());
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Doctor_Sees_Only_Appointment_Linked_Patients()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        await SeedDoctorMembershipAsync(harness, data);
        await SeedAppointmentAsync(
            harness,
            data,
            data.PatientInAId,
            data.ClinicAStaffMemberId,
            AppointmentStatus.Confirmed);

        var unrelated = await SeedExtraPatientInClinicAAsync(harness, data);

        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest());
        result.Items.Should().ContainSingle(i => i.PatientId == data.PatientInAId);
        result.Items.Should().NotContain(i => i.PatientId == unrelated);
    }

    [Fact]
    public async Task Doctor_Cannot_Access_Peer_Only_Patient()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        await SeedDoctorMembershipAsync(harness, data);

        var peerUserId = Guid.NewGuid();
        var peerStaffId = Guid.NewGuid();
        harness.Db.StaffMembers.Add(new StaffMember
        {
            Id = peerStaffId,
            UserId = peerUserId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.Doctor,
            IsActive = true,
        });
        await harness.Db.SaveChangesAsync();

        await SeedAppointmentAsync(
            harness,
            data,
            data.PatientInAId,
            peerStaffId,
            AppointmentStatus.Confirmed);

        var doctor = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var list = await doctor.SearchAsync(new StaffPatientSearchRequest());
        list.Items.Should().BeEmpty();

        await FluentActions.Awaiting(() => doctor.GetByPatientIdAsync(data.PatientInAId))
            .Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Doctor_Retains_Historical_Access_For_Completed_And_Cancelled()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        await SeedDoctorMembershipAsync(harness, data);

        var completedPatient = await SeedExtraPatientInClinicAAsync(harness, data);
        var cancelledPatient = await SeedExtraPatientInClinicAAsync(harness, data);

        await SeedAppointmentAsync(
            harness, data, completedPatient, data.ClinicAStaffMemberId, AppointmentStatus.Completed);
        await SeedAppointmentAsync(
            harness, data, cancelledPatient, data.ClinicAStaffMemberId, AppointmentStatus.CancelledByClinic);

        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest());
        result.Items.Select(i => i.PatientId).Should().BeEquivalentTo([completedPatient, cancelledPatient]);

        var completedDetail = await sut.GetByPatientIdAsync(completedPatient);
        completedDetail.PatientId.Should().Be(completedPatient);

        var cancelledDetail = await sut.GetByPatientIdAsync(cancelledPatient);
        cancelledDetail.PatientId.Should().Be(cancelledPatient);
    }

    [Fact]
    public async Task Clinic_Admin_Still_Sees_Clinic_Wide_Patients()
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
        result.Items.Should().ContainSingle(i => i.PatientId == data.PatientInAId);
        result.Items.Should().NotContain(i => i.PatientId == data.PatientInBId);
    }

    private static async Task SeedDoctorMembershipAsync(
        StaffPatientHarness harness,
        StaffPatientHarness.SeedData data)
    {
        harness.Db.StaffMembers.Add(new StaffMember
        {
            Id = data.ClinicAStaffMemberId,
            UserId = data.ClinicAStaffUserId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.Doctor,
            IsActive = true,
        });
        await harness.Db.SaveChangesAsync();
    }

    private static async Task SeedAppointmentAsync(
        StaffPatientHarness harness,
        StaffPatientHarness.SeedData data,
        Guid patientId,
        Guid doctorStaffMemberId,
        AppointmentStatus status)
    {
        var clinicPatientId = await harness.Db.ClinicPatients
            .Where(cp => cp.ClinicId == data.ClinicAId && cp.PatientId == patientId)
            .Select(cp => cp.Id)
            .SingleAsync();
        var now = DateTimeOffset.UtcNow;
        harness.Db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            PatientId = patientId,
            ClinicPatientId = clinicPatientId,
            DoctorStaffMemberId = doctorStaffMemberId,
            AppointmentDateUtc = now.AddDays(-1),
            DurationMinutes = 30,
            Status = status,
            Source = AppointmentSource.Staff,
            CreatedByUserId = data.ClinicAStaffUserId,
            Version = 0,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        });
        await harness.Db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedExtraPatientInClinicAAsync(
        StaffPatientHarness harness,
        StaffPatientHarness.SeedData data)
    {
        var patientId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        harness.Db.Patients.Add(new Patient
        {
            Id = patientId,
            FirstName = "Extra",
            LastName = "Patient",
            IsActive = true,
        });
        harness.Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = data.ClinicAId,
            PatientId = patientId,
            LocalPatientNumber = $"A-{Guid.NewGuid():N}"[..10],
            Status = ClinicPatientStatus.Active,
            RegisteredAtUtc = now,
            UpdatedAtUtc = now,
        });
        await harness.Db.SaveChangesAsync();
        return patientId;
    }
}
