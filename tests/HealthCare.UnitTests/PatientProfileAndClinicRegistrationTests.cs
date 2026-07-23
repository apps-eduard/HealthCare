using FluentAssertions;
using FluentValidation.TestHelper;
using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class PatientProfileUpdateTests
{
    [Fact]
    public async Task Patient_Updates_Own_Profile_And_Omits_Leave_Unchanged()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var (userId, patientId) = await harness.SeedLinkedPatientAsync("Ada", "Lovelace");
        var sut = harness.CreatePatientService(userId, patientId);

        var updated = await sut.UpdateCurrentPatientProfileAsync(new UpdatePatientProfileRequest
        {
            ExpectedVersion = 0,
            FirstName = "Augusta",
            Address = "12 Analytical Engine Rd",
        });

        updated.FirstName.Should().Be("Augusta");
        updated.LastName.Should().Be("Lovelace");
        updated.Address.Should().Be("12 Analytical Engine Rd");
        updated.Version.Should().Be(1);

        var reloaded = await harness.Db.Patients.SingleAsync(p => p.Id == patientId);
        reloaded.FirstName.Should().Be("Augusta");
        reloaded.LastName.Should().Be("Lovelace");
        reloaded.Version.Should().Be(1);
    }

    [Fact]
    public void Empty_Patch_Is_Rejected_By_Validator()
    {
        var validator = new UpdatePatientProfileRequestValidator();
        var result = validator.TestValidate(new UpdatePatientProfileRequest { ExpectedVersion = 0 });
        result.ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Disallowed_Identity_Fields_Are_Not_On_Update_Contract()
    {
        typeof(UpdatePatientProfileRequest).GetProperty("PatientId").Should().BeNull();
        typeof(UpdatePatientProfileRequest).GetProperty("UserId").Should().BeNull();
        typeof(UpdatePatientProfileRequest).GetProperty("Email").Should().BeNull();
        typeof(UpdatePatientProfileRequest).GetProperty("Role").Should().BeNull();
        typeof(UpdatePatientProfileRequest).GetProperty("OrganizationId").Should().BeNull();
        typeof(UpdatePatientProfileRequest).GetProperty("ClinicId").Should().BeNull();
        typeof(UpdatePatientProfileRequest).GetProperty("IsActive").Should().BeNull();
    }

    [Fact]
    public async Task Unlinked_Patient_Is_Denied()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var sut = harness.CreatePatientService(Guid.NewGuid(), linkedPatientId: null);
        var act = () => sut.UpdateCurrentPatientProfileAsync(new UpdatePatientProfileRequest
        {
            ExpectedVersion = 0,
            FirstName = "Nope",
        });
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Staff_Cannot_Use_Self_Update_Service()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var (_, patientId) = await harness.SeedLinkedPatientAsync();
        var staffUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
        };
        var sut = new PatientService(
            harness.Db,
            staffUser,
            new FakeCurrentStaff { HasActiveMembership = true, ClinicId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Role = AppRoles.Doctor, StaffMemberId = Guid.NewGuid() },
            new FakeCurrentPatient { HasLinkedPatient = false },
            new TenantAccessService(staffUser, new FakeCurrentStaff(), new FakeCurrentPatient(), NullLogger<TenantAccessService>.Instance),
            NullLogger<PatientService>.Instance);

        var act = () => sut.UpdateCurrentPatientProfileAsync(new UpdatePatientProfileRequest
        {
            ExpectedVersion = 0,
            FirstName = "Nope",
        });
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Concurrency_Conflict_When_Expected_Version_Mismatch()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var (userId, patientId) = await harness.SeedLinkedPatientAsync();
        var sut = harness.CreatePatientService(userId, patientId);

        await sut.UpdateCurrentPatientProfileAsync(new UpdatePatientProfileRequest
        {
            ExpectedVersion = 0,
            FirstName = "First",
        });

        var act = () => sut.UpdateCurrentPatientProfileAsync(new UpdatePatientProfileRequest
        {
            ExpectedVersion = 0,
            FirstName = "Stale",
        });
        await act.Should().ThrowAsync<PatientConcurrencyException>();
    }
}

public sealed class PatientClinicSelfRegistrationTests
{
    [Fact]
    public async Task Trusted_Clinic_Code_Creates_Enrollment_Idempotently()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var (userId, patientId) = await harness.SeedLinkedPatientAsync();
        var (_, clinicId, slug) = await harness.SeedClinicAsync();
        var sut = harness.CreateRegistrationService(userId, patientId);

        var first = await sut.RegisterCurrentPatientWithClinicAsync(new RegisterPatientWithClinicRequest
        {
            ClinicCode = slug,
        });
        var second = await sut.RegisterCurrentPatientWithClinicAsync(new RegisterPatientWithClinicRequest
        {
            ClinicCode = slug,
        });

        first.AlreadyEnrolled.Should().BeFalse();
        second.AlreadyEnrolled.Should().BeTrue();
        second.LocalPatientNumber.Should().Be(first.LocalPatientNumber);
        second.ClinicId.Should().Be(clinicId);
        (await harness.Db.ClinicPatients.CountAsync(cp => cp.PatientId == patientId)).Should().Be(1);
    }

    [Fact]
    public async Task Invalid_Clinic_Code_Is_Rejected_Safely()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var (userId, patientId) = await harness.SeedLinkedPatientAsync();
        var sut = harness.CreateRegistrationService(userId, patientId);

        var act = () => sut.RegisterCurrentPatientWithClinicAsync(new RegisterPatientWithClinicRequest
        {
            ClinicCode = "does-not-exist",
        });
        await act.Should().ThrowAsync<PatientClinicRegistrationException>()
            .Where(e => e.ErrorCode == PatientErrorCodes.ClinicCodeInvalid);
    }

    [Fact]
    public async Task Inactive_Clinic_Is_Rejected()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var (userId, patientId) = await harness.SeedLinkedPatientAsync();
        var (_, clinicId, slug) = await harness.SeedClinicAsync();
        var clinic = await harness.Db.Clinics.SingleAsync(c => c.Id == clinicId);
        clinic.IsActive = false;
        await harness.Db.SaveChangesAsync();

        var sut = harness.CreateRegistrationService(userId, patientId);
        var act = () => sut.RegisterCurrentPatientWithClinicAsync(new RegisterPatientWithClinicRequest
        {
            ClinicCode = slug,
        });
        await act.Should().ThrowAsync<PatientClinicRegistrationException>()
            .Where(e => e.ErrorCode == PatientErrorCodes.ClinicInactive);
    }

    [Fact]
    public async Task Inactive_Organization_Is_Rejected()
    {
        await using var harness = await ProfileTestHarness.CreateAsync();
        var (userId, patientId) = await harness.SeedLinkedPatientAsync();
        var (orgId, _, slug) = await harness.SeedClinicAsync();
        var org = await harness.Db.Organizations.SingleAsync(o => o.Id == orgId);
        org.Status = OrganizationStatus.Inactive;
        await harness.Db.SaveChangesAsync();

        var sut = harness.CreateRegistrationService(userId, patientId);
        var act = () => sut.RegisterCurrentPatientWithClinicAsync(new RegisterPatientWithClinicRequest
        {
            ClinicCode = slug,
        });
        await act.Should().ThrowAsync<PatientClinicRegistrationException>()
            .Where(e => e.ErrorCode == PatientErrorCodes.OrganizationInactive);
    }

    [Fact]
    public void Client_Cannot_Supply_PatientId_Or_ClinicId_On_Register_Contract()
    {
        typeof(RegisterPatientWithClinicRequest).GetProperty("PatientId").Should().BeNull();
        typeof(RegisterPatientWithClinicRequest).GetProperty("ClinicId").Should().BeNull();
        typeof(RegisterPatientWithClinicRequest).GetProperty("OrganizationId").Should().BeNull();
        typeof(RegisterPatientWithClinicRequest).GetProperty("LocalPatientNumber").Should().BeNull();
    }
}

internal sealed class ProfileTestHarness : IAsyncDisposable
{
    private ProfileTestHarness(HealthCareDbContext db)
    {
        Db = db;
    }

    public HealthCareDbContext Db { get; }

    public static async Task<ProfileTestHarness> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new HealthCareDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new ProfileTestHarness(db);
    }

    public async Task<(Guid UserId, Guid PatientId)> SeedLinkedPatientAsync(
        string firstName = "Pat",
        string lastName = "Ent")
    {
        var userId = Guid.NewGuid();
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            Version = 0,
        };
        Db.Patients.Add(patient);
        await Db.SaveChangesAsync();
        return (userId, patient.Id);
    }

    public async Task<(Guid OrgId, Guid ClinicId, string Slug)> SeedClinicAsync(string slug = "public-clinic-a")
    {
        var orgId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        Db.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = "Org",
            Slug = $"org-{orgId:N}"[..20],
            Status = OrganizationStatus.Active,
        });
        Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicId,
            OrganizationId = orgId,
            Name = "Clinic",
            Slug = slug,
            IsActive = true,
        });
        await Db.SaveChangesAsync();
        return (orgId, clinicId, slug);
    }

    public PatientService CreatePatientService(Guid userId, Guid? linkedPatientId)
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = [AppRoles.Patient],
            PatientId = linkedPatientId,
        };
        var patient = new FakeCurrentPatient
        {
            HasLinkedPatient = linkedPatientId.HasValue,
            PatientId = linkedPatientId,
        };
        var staff = new FakeCurrentStaff();
        var tenant = new TenantAccessService(user, staff, patient, NullLogger<TenantAccessService>.Instance);
        return new PatientService(Db, user, staff, patient, tenant, NullLogger<PatientService>.Instance);
    }

    public PatientClinicRegistrationService CreateRegistrationService(Guid userId, Guid patientId)
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = [AppRoles.Patient],
            PatientId = patientId,
        };
        var patient = new FakeCurrentPatient { HasLinkedPatient = true, PatientId = patientId };
        return new PatientClinicRegistrationService(
            Db,
            user,
            patient,
            new ClinicPublicLookup(Db),
            new LocalPatientNumberGenerator(Db, NullLogger<LocalPatientNumberGenerator>.Instance),
            NullLogger<PatientClinicRegistrationService>.Instance);
    }

    public ValueTask DisposeAsync()
    {
        Db.Dispose();
        return ValueTask.CompletedTask;
    }
}
