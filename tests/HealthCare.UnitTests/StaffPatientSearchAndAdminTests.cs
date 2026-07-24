using FluentAssertions;
using FluentValidation.TestHelper;
using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class StaffPatientSearchAndAdminTests
{
    [Fact]
    public async Task Clinic_Staff_Sees_Only_Own_Clinic_Patients()
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

        result.Items.Should().ContainSingle(i => i.PatientId == data.PatientInAId);
        result.Items.Should().NotContain(i => i.PatientId == data.PatientInBId);
        result.Items[0].LocalPatientNumber.Should().Be("A-0001");
    }

    [Fact]
    public async Task Clinic_Staff_Cannot_See_Other_Clinic_Patients()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var act = () => sut.GetByPatientIdAsync(data.PatientInBId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Organization_Admin_Sees_Patients_Across_Org_Clinics()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.OrgAdminUserId,
            AppRoles.OrganizationAdmin,
            data.Org1Id,
            data.ClinicAId,
            data.OrgAdminStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest());

        result.Items.Select(i => i.PatientId).Should().BeEquivalentTo([data.PatientInAId, data.PatientInBId]);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Search_Other_Organization()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.OrgAdminUserId,
            AppRoles.OrganizationAdmin,
            data.Org1Id,
            data.ClinicAId,
            data.OrgAdminStaffMemberId);

        var act = () => sut.GetByPatientIdAsync(data.PatientInOtherOrgId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Patient_Role_Is_Denied()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Patient],
        };
        var sut = new StaffPatientService(
            harness.Db,
            user,
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger(),
            NullLogger<StaffPatientService>.Instance);

        var act = () => sut.SearchAsync(new StaffPatientSearchRequest());
        await act.Should().ThrowAsync<AuthorizationException>()
            .Where(e => e.ErrorCode == AuthorizationErrorCodes.Forbidden
                        || e.StatusCode == 403);
    }

    [Fact]
    public async Task Inactive_Staff_Membership_Is_Denied()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
        };
        var sut = new StaffPatientService(
            harness.Db,
            user,
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger(),
            NullLogger<StaffPatientService>.Instance);

        var act = () => sut.SearchAsync(new StaffPatientSearchRequest());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Client_Supplied_ClinicId_Is_Ignored_For_Clinic_Staff()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest
        {
            ClinicId = data.ClinicBId,
        });

        result.Items.Should().ContainSingle(i => i.PatientId == data.PatientInAId);
        result.Items.Should().NotContain(i => i.ClinicId == data.ClinicBId);
    }

    [Fact]
    public async Task Client_OrganizationId_Is_Not_On_Search_Contract()
    {
        typeof(StaffPatientSearchRequest).GetProperty("OrganizationId").Should().BeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Search_By_Name_Works()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest { Search = "Alice" });
        result.Items.Should().ContainSingle(i => i.FirstName == "Alice");
    }

    [Fact]
    public async Task Search_By_Local_Patient_Number_Works()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest
        {
            LocalPatientNumber = "A-0001",
        });
        result.Items.Should().ContainSingle();
        result.Items[0].LocalPatientNumber.Should().Be("A-0001");
    }

    [Fact]
    public async Task Status_Filtering_Works()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var inactive = await harness.Db.ClinicPatients.SingleAsync(cp =>
            cp.ClinicId == data.ClinicAId && cp.PatientId == data.PatientInAId);
        inactive.Status = ClinicPatientStatus.Inactive;
        await harness.Db.SaveChangesAsync();

        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var activeOnly = await sut.SearchAsync(new StaffPatientSearchRequest
        {
            ClinicPatientStatus = "Active",
        });
        activeOnly.Items.Should().BeEmpty();

        var inactiveOnly = await sut.SearchAsync(new StaffPatientSearchRequest
        {
            ClinicPatientStatus = "Inactive",
        });
        inactiveOnly.Items.Should().ContainSingle();
    }

    [Fact]
    public void Pagination_Defaults_And_Max_Page_Size_Are_Validated()
    {
        var validator = new StaffPatientSearchRequestValidator();
        validator.TestValidate(new StaffPatientSearchRequest { Page = 1, PageSize = 20 })
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new StaffPatientSearchRequest { Page = 0, PageSize = 20 })
            .ShouldHaveValidationErrorFor(x => x.Page);
        validator.TestValidate(new StaffPatientSearchRequest { Page = 1, PageSize = 101 })
            .ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public async Task Stable_Sorting_Uses_Secondary_Id()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        await harness.SeedExtraClinicAPatientsAsync(data.ClinicAId, count: 3);

        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var result = await sut.SearchAsync(new StaffPatientSearchRequest
        {
            SortBy = "lastName",
            SortDirection = "asc",
            PageSize = 50,
        });

        result.Items.Select(i => i.LastName).Should().BeInAscendingOrder();
        result.Items.Select(i => i.ClinicPatientId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Invalid_Sort_Field_Is_Rejected()
    {
        var validator = new StaffPatientSearchRequestValidator();
        validator.TestValidate(new StaffPatientSearchRequest { SortBy = "passwordHash" })
            .ShouldHaveValidationErrorFor(x => x.SortBy);
    }

    [Fact]
    public async Task Patient_Detail_Allowed_Within_Scope()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var detail = await sut.GetByPatientIdAsync(data.PatientInAId);
        detail.PatientId.Should().Be(data.PatientInAId);
        detail.LocalPatientNumber.Should().Be("A-0001");
    }

    [Fact]
    public async Task Patient_Detail_Denied_Outside_Scope()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var act = () => sut.GetByPatientIdAsync(data.PatientInBId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task ClinicPatient_Update_Allowed_Within_Scope()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var updated = await sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest { ExpectedVersion = 0, Status = "Inactive" });

        updated.ClinicPatientStatus.Should().Be("Inactive");
        updated.Version.Should().Be(1);
    }

    [Fact]
    public async Task Update_Denied_Across_Clinic()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var act = () => sut.UpdateClinicProfileAsync(
            data.PatientInBId,
            new UpdateClinicPatientRequest { ExpectedVersion = 0, Status = "Inactive" });
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public void Protected_Fields_Are_Not_On_Update_Contract()
    {
        typeof(UpdateClinicPatientRequest).GetProperty("LocalPatientNumber").Should().BeNull();
        typeof(UpdateClinicPatientRequest).GetProperty("PatientId").Should().BeNull();
        typeof(UpdateClinicPatientRequest).GetProperty("UserId").Should().BeNull();
        typeof(UpdateClinicPatientRequest).GetProperty("Email").Should().BeNull();
        typeof(UpdateClinicPatientRequest).GetProperty("FirstName").Should().BeNull();
        typeof(UpdateClinicPatientRequest).GetProperty("OrganizationId").Should().BeNull();
        // ClinicId is allowed only as enrollment targeting for ORGANIZATION_ADMIN.
        typeof(UpdateClinicPatientRequest).GetProperty("ClinicId").Should().NotBeNull();
    }

    [Fact]
    public async Task Organization_Admin_ClinicId_Filter_Is_Validated_In_Org()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.OrgAdminUserId,
            AppRoles.OrganizationAdmin,
            data.Org1Id,
            data.ClinicAId,
            data.OrgAdminStaffMemberId);

        var filtered = await sut.SearchAsync(new StaffPatientSearchRequest { ClinicId = data.ClinicBId });
        filtered.Items.Should().ContainSingle(i => i.PatientId == data.PatientInBId);

        var deny = () => sut.SearchAsync(new StaffPatientSearchRequest { ClinicId = data.OtherOrgClinicId });
        await deny.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Organization_Admin_Detail_Lists_Org_Enrollments()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        await harness.EnrollPatientInSecondClinicAsync(data.PatientInAId, data.ClinicBId, "B-A001");

        var sut = harness.CreateService(
            data.OrgAdminUserId,
            AppRoles.OrganizationAdmin,
            data.Org1Id,
            data.ClinicAId,
            data.OrgAdminStaffMemberId);

        var detail = await sut.GetByPatientIdAsync(data.PatientInAId);
        detail.Enrollments.Should().HaveCount(2);
        detail.Enrollments.Select(e => e.ClinicId).Should().BeEquivalentTo([data.ClinicAId, data.ClinicBId]);
    }

    [Fact]
    public async Task Organization_Admin_Can_Update_Enrollment_By_ClinicId()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        await harness.EnrollPatientInSecondClinicAsync(data.PatientInAId, data.ClinicBId, "B-A001");

        var sut = harness.CreateService(
            data.OrgAdminUserId,
            AppRoles.OrganizationAdmin,
            data.Org1Id,
            data.ClinicAId,
            data.OrgAdminStaffMemberId);

        var updated = await sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest
            {
                ClinicId = data.ClinicBId,
                ExpectedVersion = 0,
                Status = "Inactive",
            });

        updated.ClinicId.Should().Be(data.ClinicBId);
        updated.ClinicPatientStatus.Should().Be("Inactive");

        var clinicA = await harness.Db.ClinicPatients.SingleAsync(cp =>
            cp.ClinicId == data.ClinicAId && cp.PatientId == data.PatientInAId);
        clinicA.Status.Should().Be(ClinicPatientStatus.Active);
    }

    [Fact]
    public async Task Appointment_Lookup_Returns_Only_Active_Enrollment()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var inactive = await harness.Db.ClinicPatients.SingleAsync(cp =>
            cp.ClinicId == data.ClinicAId && cp.PatientId == data.PatientInAId);
        inactive.Status = ClinicPatientStatus.Inactive;
        await harness.Db.SaveChangesAsync();

        var sut = harness.CreateService(
            data.OrgAdminUserId,
            AppRoles.OrganizationAdmin,
            data.Org1Id,
            data.ClinicAId,
            data.OrgAdminStaffMemberId);

        var lookup = await sut.LookupForAppointmentAsync(new StaffPatientLookupRequest
        {
            ClinicId = data.ClinicAId,
        });
        lookup.Items.Should().BeEmpty();

        var withoutClinic = () => sut.LookupForAppointmentAsync(new StaffPatientLookupRequest());
        await withoutClinic.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Organization_Admin_Can_Enroll_Into_Sibling_Clinic()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = data.OrgAdminUserId,
            Roles = [AppRoles.OrganizationAdmin],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = data.OrgAdminStaffMemberId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.OrganizationAdmin,
        };
        var numbers = new LocalPatientNumberGenerator(harness.Db, NullLogger<LocalPatientNumberGenerator>.Instance);
        var enrollment = new ClinicEnrollmentService(
            harness.Db,
            user,
            staff,
            new NoOpAuthorizationAuditLogger(),
            numbers,
            NullLogger<ClinicEnrollmentService>.Instance);

        var result = await enrollment.EnrollAsync(data.ClinicBId, data.PatientInAId);
        result.AlreadyEnrolled.Should().BeFalse();
        result.ClinicId.Should().Be(data.ClinicBId);

        var deny = () => enrollment.EnrollAsync(data.OtherOrgClinicId, data.PatientInAId);
        await deny.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Concurrency_Conflict_Is_Detected()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var act = () => sut.UpdateClinicProfileAsync(
            data.PatientInAId,
            new UpdateClinicPatientRequest { ExpectedVersion = 99, Status = "Inactive" });
        await act.Should().ThrowAsync<ClinicPatientConcurrencyException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Bypass_And_ClinicId()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        var sut = new StaffPatientService(
            harness.Db,
            user,
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger(),
            NullLogger<StaffPatientService>.Instance);

        var withoutBypass = () => sut.SearchAsync(new StaffPatientSearchRequest { ClinicId = data.ClinicAId });
        await withoutBypass.Should().ThrowAsync<AuthorizationException>();

        var withoutClinic = () => sut.SearchAsync(
            new StaffPatientSearchRequest(),
            PlatformAdminBypass.Explicit);
        await withoutClinic.Should().ThrowAsync<AuthorizationException>();

        var withBypass = await sut.SearchAsync(
            new StaffPatientSearchRequest { ClinicId = data.ClinicAId },
            PlatformAdminBypass.Explicit);
        withBypass.Items.Should().Contain(i => i.PatientId == data.PatientInAId);
    }

    [Fact]
    public async Task Pagination_Metadata_Is_Correct()
    {
        await using var harness = await StaffPatientHarness.CreateAsync();
        var data = await harness.SeedTwoClinicsAsync();
        await harness.SeedExtraClinicAPatientsAsync(data.ClinicAId, count: 5);
        var sut = harness.CreateService(
            data.ClinicAStaffUserId,
            AppRoles.Doctor,
            data.Org1Id,
            data.ClinicAId,
            data.ClinicAStaffMemberId);

        var page = await sut.SearchAsync(new StaffPatientSearchRequest { Page = 1, PageSize = 2 });
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.TotalCount.Should().Be(6);
        page.TotalPages.Should().Be(3);
        page.Items.Should().HaveCount(2);

        var outOfRange = await sut.SearchAsync(new StaffPatientSearchRequest { Page = 99, PageSize = 2 });
        outOfRange.Items.Should().BeEmpty();
        outOfRange.TotalCount.Should().Be(6);
    }
}

internal sealed class StaffPatientHarness : IAsyncDisposable
{
    private StaffPatientHarness(HealthCareDbContext db) => Db = db;

    public HealthCareDbContext Db { get; }

    public static async Task<StaffPatientHarness> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new HealthCareDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new StaffPatientHarness(db);
    }

    public async Task<SeedData> SeedTwoClinicsAsync()
    {
        var org1 = Guid.NewGuid();
        var org2 = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var clinicOtherOrg = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        Db.Organizations.AddRange(
            new Organization { Id = org1, Name = "Org1", Slug = "org1", Status = OrganizationStatus.Active },
            new Organization { Id = org2, Name = "Org2", Slug = "org2", Status = OrganizationStatus.Active });
        Db.Clinics.AddRange(
            new Domain.Clinics.Clinic { Id = clinicA, OrganizationId = org1, Name = "A", Slug = "a", IsActive = true },
            new Domain.Clinics.Clinic { Id = clinicB, OrganizationId = org1, Name = "B", Slug = "b", IsActive = true },
            new Domain.Clinics.Clinic { Id = clinicOtherOrg, OrganizationId = org2, Name = "X", Slug = "x", IsActive = true });

        var patientA = new Patient { Id = Guid.NewGuid(), FirstName = "Alice", LastName = "Anders", IsActive = true };
        var patientB = new Patient { Id = Guid.NewGuid(), FirstName = "Bob", LastName = "Baker", IsActive = true };
        var patientOther = new Patient { Id = Guid.NewGuid(), FirstName = "Eve", LastName = "Other", IsActive = true };
        Db.Patients.AddRange(patientA, patientB, patientOther);

        Db.ClinicPatients.AddRange(
            new ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicA,
                PatientId = patientA.Id,
                LocalPatientNumber = "A-0001",
                Status = ClinicPatientStatus.Active,
                RegisteredAtUtc = now,
                UpdatedAtUtc = now,
            },
            new ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicB,
                PatientId = patientB.Id,
                LocalPatientNumber = "B-0001",
                Status = ClinicPatientStatus.Active,
                RegisteredAtUtc = now,
                UpdatedAtUtc = now,
            },
            new ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicOtherOrg,
                PatientId = patientOther.Id,
                LocalPatientNumber = "X-0001",
                Status = ClinicPatientStatus.Active,
                RegisteredAtUtc = now,
                UpdatedAtUtc = now,
            });

        await Db.SaveChangesAsync();

        return new SeedData(
            org1,
            org2,
            clinicA,
            clinicB,
            clinicOtherOrg,
            patientA.Id,
            patientB.Id,
            patientOther.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    public async Task SeedExtraClinicAPatientsAsync(Guid clinicAId, int count)
    {
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            var patient = new Patient
            {
                Id = Guid.NewGuid(),
                FirstName = $"P{i}",
                LastName = $"Z{i:D2}",
                IsActive = true,
            };
            Db.Patients.Add(patient);
            Db.ClinicPatients.Add(new ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicAId,
                PatientId = patient.Id,
                LocalPatientNumber = $"A-X{i:D3}",
                Status = ClinicPatientStatus.Active,
                RegisteredAtUtc = now.AddMinutes(i),
                UpdatedAtUtc = now,
            });
        }

        await Db.SaveChangesAsync();
    }

    public StaffPatientService CreateService(
        Guid userId,
        string role,
        Guid organizationId,
        Guid clinicId,
        Guid staffMemberId)
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = [role],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = staffMemberId,
            OrganizationId = organizationId,
            ClinicId = clinicId,
            Role = role,
        };
        return new StaffPatientService(
            Db,
            user,
            staff,
            new NoOpAuthorizationAuditLogger(),
            NullLogger<StaffPatientService>.Instance);
    }

    public async Task EnrollPatientInSecondClinicAsync(Guid patientId, Guid clinicId, string localNumber)
    {
        var now = DateTimeOffset.UtcNow;
        Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patientId,
            LocalPatientNumber = localNumber,
            Status = ClinicPatientStatus.Active,
            RegisteredAtUtc = now,
            UpdatedAtUtc = now,
        });
        await Db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        Db.Dispose();
        return ValueTask.CompletedTask;
    }

    public sealed record SeedData(
        Guid Org1Id,
        Guid Org2Id,
        Guid ClinicAId,
        Guid ClinicBId,
        Guid OtherOrgClinicId,
        Guid PatientInAId,
        Guid PatientInBId,
        Guid PatientInOtherOrgId,
        Guid ClinicAStaffUserId,
        Guid ClinicAStaffMemberId,
        Guid OrgAdminUserId,
        Guid OrgAdminStaffMemberId);
}
