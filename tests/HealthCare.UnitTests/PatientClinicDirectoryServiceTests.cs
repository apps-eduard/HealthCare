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

public sealed class PatientClinicDirectoryServiceTests
{
    [Fact]
    public async Task Search_Returns_Only_Active_Clinics_In_Active_Organizations()
    {
        await using var h = await DirectoryHarness.CreateAsync();
        var (userId, patientId) = await h.SeedLinkedPatientAsync();
        await h.SeedClinicAsync(slug: "active-a", name: "Alpha Care", city: "Riyadh", specialty: "General");
        await h.SeedClinicAsync(slug: "inactive-b", name: "Beta Care", city: "Jeddah", isActive: false);
        var (inactiveOrgId, _, _) = await h.SeedClinicAsync(slug: "org-inactive", name: "Gamma Care", city: "Dammam");
        var inactiveOrg = await h.Db.Organizations.SingleAsync(o => o.Id == inactiveOrgId);
        inactiveOrg.Status = OrganizationStatus.Inactive;
        await h.Db.SaveChangesAsync();

        var sut = h.CreateDirectoryService(userId, patientId);
        var page = await sut.SearchAsync(new PatientClinicSearchRequest());

        page.Items.Should().ContainSingle(c => c.ClinicCode == "active-a");
        page.Items.Should().NotContain(c => c.ClinicCode == "inactive-b");
        page.Items.Should().NotContain(c => c.ClinicCode == "org-inactive");
    }

    [Fact]
    public async Task Search_Matches_Name_City_And_Specialty()
    {
        await using var h = await DirectoryHarness.CreateAsync();
        var (userId, patientId) = await h.SeedLinkedPatientAsync();
        await h.SeedClinicAsync(slug: "north", name: "North Clinic", city: "Riyadh", specialty: "Cardiology");
        await h.SeedClinicAsync(slug: "south", name: "South Clinic", city: "Jeddah", specialty: "Dermatology");

        var sut = h.CreateDirectoryService(userId, patientId);

        (await sut.SearchAsync(new PatientClinicSearchRequest { Search = "north" }))
            .Items.Should().ContainSingle(c => c.ClinicCode == "north");
        (await sut.SearchAsync(new PatientClinicSearchRequest { Search = "jeddah" }))
            .Items.Should().ContainSingle(c => c.ClinicCode == "south");
        (await sut.SearchAsync(new PatientClinicSearchRequest { Specialty = "cardio" }))
            .Items.Should().ContainSingle(c => c.ClinicCode == "north");
    }

    [Fact]
    public async Task Search_Paginates_With_Stable_Name_Order()
    {
        await using var h = await DirectoryHarness.CreateAsync();
        var (userId, patientId) = await h.SeedLinkedPatientAsync();
        await h.SeedClinicAsync(slug: "c", name: "Charlie", city: "A");
        await h.SeedClinicAsync(slug: "a", name: "Alpha", city: "B");
        await h.SeedClinicAsync(slug: "b", name: "Bravo", city: "C");

        var sut = h.CreateDirectoryService(userId, patientId);
        var page1 = await sut.SearchAsync(new PatientClinicSearchRequest { Page = 1, PageSize = 2 });
        var page2 = await sut.SearchAsync(new PatientClinicSearchRequest { Page = 2, PageSize = 2 });

        page1.Items.Select(i => i.Name).Should().Equal("Alpha", "Bravo");
        page2.Items.Select(i => i.Name).Should().Equal("Charlie");
        page1.TotalCount.Should().Be(3);
        page1.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task Search_Caps_PageSize_And_Marks_Enrollment()
    {
        await using var h = await DirectoryHarness.CreateAsync();
        var (userId, patientId) = await h.SeedLinkedPatientAsync();
        var (_, clinicId, slug) = await h.SeedClinicAsync(slug: "enrolled", name: "Enrolled Clinic");
        h.Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patientId,
            LocalPatientNumber = "P-1",
            Status = ClinicPatientStatus.Active,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateDirectoryService(userId, patientId);
        var page = await sut.SearchAsync(new PatientClinicSearchRequest { PageSize = 500 });

        page.PageSize.Should().Be(PatientClinicSearchRequestValidator.MaxPageSize);
        page.Items.Single(c => c.ClinicCode == slug).IsEnrolled.Should().BeTrue();
    }

    [Fact]
    public async Task Detail_Returns_Patient_Safe_Fields_Only()
    {
        await using var h = await DirectoryHarness.CreateAsync();
        var (userId, patientId) = await h.SeedLinkedPatientAsync();
        await h.SeedClinicAsync(
            slug: "detail-a",
            name: "Detail Clinic",
            city: "Riyadh",
            specialty: "General",
            description: "Public description",
            phone: "+966100",
            email: "clinic@example.com",
            address: "1 Main St");

        var sut = h.CreateDirectoryService(userId, patientId);
        var detail = await sut.GetByClinicCodeAsync("detail-a");

        detail.ClinicCode.Should().Be("detail-a");
        detail.Name.Should().Be("Detail Clinic");
        detail.City.Should().Be("Riyadh");
        detail.Specialty.Should().Be("General");
        detail.Description.Should().Be("Public description");
        detail.PhoneNumber.Should().Be("+966100");
        detail.Email.Should().Be("clinic@example.com");
        detail.Address.Should().Be("1 Main St");
        detail.IsEnrolled.Should().BeFalse();

        typeof(PatientClinicDetailResponse).GetProperty("OrganizationId").Should().BeNull();
        typeof(PatientClinicDetailResponse).GetProperty("ClinicId").Should().BeNull();
        typeof(PatientClinicDetailResponse).GetProperty("CreatedAtUtc").Should().BeNull();
        typeof(PatientClinicListItemResponse).GetProperty("OrganizationId").Should().BeNull();
        typeof(PatientClinicListItemResponse).GetProperty("ClinicId").Should().BeNull();
    }

    [Fact]
    public async Task Detail_Unknown_Or_Inactive_Is_Concealed()
    {
        await using var h = await DirectoryHarness.CreateAsync();
        var (userId, patientId) = await h.SeedLinkedPatientAsync();
        await h.SeedClinicAsync(slug: "gone", name: "Gone", isActive: false);
        var sut = h.CreateDirectoryService(userId, patientId);

        var unknown = () => sut.GetByClinicCodeAsync("missing");
        await unknown.Should().ThrowAsync<PatientClinicRegistrationException>()
            .Where(e => e.ErrorCode == PatientErrorCodes.ClinicCodeInvalid);

        var inactive = () => sut.GetByClinicCodeAsync("gone");
        await inactive.Should().ThrowAsync<PatientClinicRegistrationException>()
            .Where(e => e.ErrorCode == PatientErrorCodes.ClinicCodeInvalid);
    }

    [Fact]
    public async Task Unlinked_Patient_Is_Denied()
    {
        await using var h = await DirectoryHarness.CreateAsync();
        var sut = h.CreateDirectoryService(Guid.NewGuid(), linkedPatientId: null);
        var act = () => sut.SearchAsync(new PatientClinicSearchRequest());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public void Validator_Enforces_Search_Length_And_Page_Bounds()
    {
        var validator = new PatientClinicSearchRequestValidator();
        validator.TestValidate(new PatientClinicSearchRequest { Search = new string('x', 101) })
            .ShouldHaveValidationErrorFor(x => x.Search);
        validator.TestValidate(new PatientClinicSearchRequest { Page = 0 })
            .ShouldHaveValidationErrorFor(x => x.Page);
        validator.TestValidate(new PatientClinicSearchRequest { PageSize = 51 })
            .ShouldHaveValidationErrorFor(x => x.PageSize);
    }
}

internal sealed class DirectoryHarness : IAsyncDisposable
{
    private DirectoryHarness(HealthCareDbContext db) => Db = db;

    public HealthCareDbContext Db { get; }

    public static async Task<DirectoryHarness> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new HealthCareDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new DirectoryHarness(db);
    }

    public async Task<(Guid UserId, Guid PatientId)> SeedLinkedPatientAsync()
    {
        var userId = Guid.NewGuid();
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FirstName = "Pat",
            LastName = "Ent",
            IsActive = true,
            Version = 0,
        };
        Db.Patients.Add(patient);
        await Db.SaveChangesAsync();
        return (userId, patient.Id);
    }

    public async Task<(Guid OrgId, Guid ClinicId, string Slug)> SeedClinicAsync(
        string slug,
        string name = "Clinic",
        string? city = null,
        string? specialty = null,
        string? description = null,
        string? phone = null,
        string? email = null,
        string? address = null,
        bool isActive = true)
    {
        var orgId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        Db.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = $"Org-{slug}",
            Slug = $"org-{orgId:N}"[..20],
            Status = OrganizationStatus.Active,
        });
        Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicId,
            OrganizationId = orgId,
            Name = name,
            Slug = slug,
            City = city,
            Specialty = specialty,
            Description = description,
            PhoneNumber = phone,
            Email = email,
            Address = address,
            IsActive = isActive,
            TimeZoneId = "Asia/Riyadh",
        });
        await Db.SaveChangesAsync();
        return (orgId, clinicId, slug);
    }

    public PatientClinicDirectoryService CreateDirectoryService(Guid userId, Guid? linkedPatientId)
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
        return new PatientClinicDirectoryService(
            Db,
            user,
            patient,
            new ClinicPublicLookup(Db),
            NullLogger<PatientClinicDirectoryService>.Instance);
    }

    public ValueTask DisposeAsync()
    {
        Db.Dispose();
        return ValueTask.CompletedTask;
    }
}
