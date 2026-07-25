using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class ClinicAdminPatientDirectoryTests
{
    [Fact]
    public async Task Clinic_Admin_Searches_Only_Own_Clinic_Patients()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = harness.CreateService(
            admin.UserId, AppRoles.ClinicAdmin, data.Org1Id, data.ClinicAId, admin.StaffId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest());
        result.Items.Should().Contain(i => i.PatientId == data.PatientInAId);
        result.Items.Should().NotContain(i => i.PatientId == data.PatientInBId);
    }

    [Fact]
    public async Task Clinic_Admin_Cross_Clinic_Detail_Denied()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = harness.CreateService(
            admin.UserId, AppRoles.ClinicAdmin, data.Org1Id, data.ClinicAId, admin.StaffId);

        var act = () => sut.GetByPatientIdAsync(data.PatientInBId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Clinic_Admin_Can_Update_Own_Clinic_Status()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = harness.CreateService(
            admin.UserId, AppRoles.ClinicAdmin, data.Org1Id, data.ClinicAId, admin.StaffId);

        var updated = await sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest { ExpectedVersion = 0, Status = "Inactive" });

        updated.ClinicPatientStatus.Should().Be("Inactive");
        var restored = await sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest { ExpectedVersion = updated.Version, Status = "Active" });
        restored.ClinicPatientStatus.Should().Be("Active");
    }

    [Fact]
    public async Task Clinic_Admin_Cross_Clinic_Status_Update_Denied()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = harness.CreateService(
            admin.UserId, AppRoles.ClinicAdmin, data.Org1Id, data.ClinicAId, admin.StaffId);

        var act = () => sut.UpdateClinicProfileAsync(
            data.PatientInBId,
            new UpdateClinicPatientRequest { ExpectedVersion = 0, Status = "Inactive" });
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Clinic_Admin_Cross_Clinic_Enrollment_Denied()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = admin.UserId,
            Roles = [AppRoles.ClinicAdmin],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = admin.StaffId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.ClinicAdmin,
        };
        var enrollment = new ClinicEnrollmentService(
            harness.Db,
            user,
            staff,
            new NoOpAuthorizationAuditLogger(),
            new LocalPatientNumberGenerator(harness.Db, NullLogger<LocalPatientNumberGenerator>.Instance),
            NullLogger<ClinicEnrollmentService>.Instance);

        var act = () => enrollment.EnrollAsync(data.ClinicBId, data.PatientInAId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Clinic_Admin_Own_Clinic_Enrollment_Is_Idempotent()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = admin.UserId,
            Roles = [AppRoles.ClinicAdmin],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = admin.StaffId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.ClinicAdmin,
        };
        var enrollment = new ClinicEnrollmentService(
            harness.Db,
            user,
            staff,
            new NoOpAuthorizationAuditLogger(),
            new LocalPatientNumberGenerator(harness.Db, NullLogger<LocalPatientNumberGenerator>.Instance),
            NullLogger<ClinicEnrollmentService>.Instance);

        var again = await enrollment.EnrollAsync(data.ClinicAId, data.PatientInAId);
        again.AlreadyEnrolled.Should().BeTrue();
        again.ClinicId.Should().Be(data.ClinicAId);
    }

    [Fact]
    public async Task Inactive_Membership_Denied_For_Clinic_Admin_Search()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = new StaffPatientService(
            harness.Db,
            new FakeCurrentUser
            {
                IsAuthenticated = true,
                UserId = admin.UserId,
                Roles = [AppRoles.ClinicAdmin],
            },
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger(),
            NullLogger<StaffPatientService>.Instance);

        var act = () => sut.SearchAsync(new StaffPatientSearchRequest());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Stale_Version_Conflict_For_Clinic_Admin()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = harness.CreateService(
            admin.UserId, AppRoles.ClinicAdmin, data.Org1Id, data.ClinicAId, admin.StaffId);

        var act = () => sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest { ExpectedVersion = 99, Status = "Inactive" });
        await act.Should().ThrowAsync<ClinicPatientConcurrencyException>();
    }

    [Fact]
    public async Task Patient_Without_Staff_Membership_Cannot_Search_Staff_Patients()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = new StaffPatientService(
            harness.Db,
            new FakeCurrentUser
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                Roles = [AppRoles.Patient],
            },
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger(),
            NullLogger<StaffPatientService>.Instance);

        var act = () => sut.SearchAsync(new StaffPatientSearchRequest { ClinicId = data.ClinicAId });
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Invalid_Clinic_Status_Is_Rejected()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = harness.CreateService(
            admin.UserId, AppRoles.ClinicAdmin, data.Org1Id, data.ClinicAId, admin.StaffId);

        var act = () => sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest { ExpectedVersion = 0, Status = "Suspended" });
        await act.Should().ThrowAsync<ClinicPatientUpdateException>();
    }

    [Fact]
    public void Invalid_Clinic_Status_Is_Rejected_By_Validator()
    {
        var validator = new UpdateClinicPatientRequestValidator();
        var result = validator.Validate(new UpdateClinicPatientRequest
        {
            ExpectedVersion = 0,
            Status = "Suspended",
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Status_Update_Audit_Uses_Safe_Operation_Name()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var audit = new RecordingPatientAuditLogger();
        var sut = new StaffPatientService(
            harness.Db,
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
            audit,
            NullLogger<StaffPatientService>.Instance);

        await sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest { ExpectedVersion = 0, Status = "Inactive" });

        audit.Operations.Should().Contain(o =>
            o.Operation == "patient_clinic_status_changed" && o.ResultCode == "succeeded");
        var json = System.Text.Json.JsonSerializer.Serialize(audit.Operations);
        json.ToLowerInvariant().Should().NotContain("password");
        json.ToLowerInvariant().Should().NotContain("token");
        json.ToLowerInvariant().Should().NotContain("medical");
        json.Should().NotContain("Inactive");
    }

    [Fact]
    public async Task Search_Does_Not_Leak_Inaccessible_Patient_Existence()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var admin = await SeedClinicAdminAsync(harness, data);
        var sut = harness.CreateService(
            admin.UserId, AppRoles.ClinicAdmin, data.Org1Id, data.ClinicAId, admin.StaffId);

        var act = () => sut.GetByPatientIdAsync(data.PatientInBId);
        await act.Should().ThrowAsync<AuthorizationException>();

        var search = await sut.SearchAsync(new StaffPatientSearchRequest { Search = "Baker" });
        search.Items.Should().NotContain(i => i.PatientId == data.PatientInBId);
    }

    [Fact]
    public void Clinic_Admin_Does_Not_Receive_Medical_Note_Permissions()
    {
        var permissions = RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin);
        permissions.Should().Contain(Permissions.Patients.Search);
        permissions.Should().Contain(Permissions.Patients.Read);
        permissions.Should().Contain(Permissions.Patients.UpdateClinicStatus);
        permissions.Should().NotContain(Permissions.MedicalNotes.Read);
        permissions.Should().NotContain(Permissions.MedicalNotes.Create);
    }

    [Fact]
    public void Clinic_Patient_Statuses_Are_Active_And_Inactive_Only()
    {
        Enum.GetNames<ClinicPatientStatus>().Should().BeEquivalentTo("Active", "Inactive");
    }

    private static async Task<(Guid UserId, Guid StaffId)> SeedClinicAdminAsync(
        StaffPatientHarness harness,
        StaffPatientHarness.SeedData data)
    {
        var userId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        harness.Db.StaffMembers.Add(new Domain.Staff.StaffMember
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

internal sealed class RecordingPatientAuditLogger : NoOpAuthorizationAuditLogger
{
    public List<(string Operation, string ResultCode, Guid? OrganizationId, Guid? ClinicId, Guid? PatientId)> Operations { get; } = [];

    public override void PatientOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? patientId = null)
    {
        Operations.Add((operation, resultCode, organizationId, clinicId, patientId));
    }
}
