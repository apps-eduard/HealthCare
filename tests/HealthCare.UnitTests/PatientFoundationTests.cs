using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class PatientAccountLinkerTests
{
    [Fact]
    public async Task Links_Patient_User_Successfully()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var user = await harness.CreateUserAsync("patient@link.test", AppRoles.Patient);
        var patient = harness.AddPatient();

        await harness.Linker.LinkUserToPatientAsync(user.Id, patient.Id);

        var reloaded = await harness.Db.Patients.SingleAsync(p => p.Id == patient.Id);
        reloaded.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Rejects_Duplicate_User_Linkage()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var user = await harness.CreateUserAsync("dup@link.test", AppRoles.Patient);
        var first = harness.AddPatient();
        var second = harness.AddPatient(firstName: "Other");
        await harness.Linker.LinkUserToPatientAsync(user.Id, first.Id);

        var act = () => harness.Linker.LinkUserToPatientAsync(user.Id, second.Id);
        await act.Should().ThrowAsync<PatientLinkageException>()
            .Where(e => e.ErrorCode == "patient.duplicate_user_linkage");
    }

    [Fact]
    public async Task Rejects_Non_Patient_Role()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var user = await harness.CreateUserAsync("doctor@link.test", AppRoles.Doctor);
        var patient = harness.AddPatient();

        var act = () => harness.Linker.LinkUserToPatientAsync(user.Id, patient.Id);
        await act.Should().ThrowAsync<PatientLinkageException>()
            .Where(e => e.ErrorCode == "patient.user_not_patient_role");
    }

    [Fact]
    public async Task Rejects_Staff_Membership_Even_With_Patient_Role()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var user = await harness.CreateUserAsync("mixed@link.test", AppRoles.Patient);
        var orgId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        harness.Db.Organizations.Add(new Domain.Organizations.Organization
        {
            Id = orgId,
            Name = "Org",
            Slug = "org-link-test",
            Status = Domain.Organizations.OrganizationStatus.Active,
        });
        harness.Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicId,
            OrganizationId = orgId,
            Name = "Clinic",
            Slug = "clinic-link-test",
            IsActive = true,
        });
        harness.Db.StaffMembers.Add(new StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrganizationId = orgId,
            ClinicId = clinicId,
            Role = AppRoles.Receptionist,
            IsActive = true,
        });
        await harness.Db.SaveChangesAsync();
        var patient = harness.AddPatient();

        var act = () => harness.Linker.LinkUserToPatientAsync(user.Id, patient.Id);
        await act.Should().ThrowAsync<PatientLinkageException>()
            .Where(e => e.ErrorCode == "patient.user_is_staff");
    }

    [Fact]
    public async Task Rejects_Linking_Already_Linked_Patient_To_Different_User()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var userA = await harness.CreateUserAsync("a@link.test", AppRoles.Patient);
        var userB = await harness.CreateUserAsync("b@link.test", AppRoles.Patient);
        var patient = harness.AddPatient();
        await harness.Linker.LinkUserToPatientAsync(userA.Id, patient.Id);

        var act = () => harness.Linker.LinkUserToPatientAsync(userB.Id, patient.Id);
        await act.Should().ThrowAsync<PatientLinkageException>()
            .Where(e => e.ErrorCode == "patient.already_linked");
    }
}

public sealed class PatientServiceAccessTests
{
    [Fact]
    public async Task Patient_Self_Access_Allowed_When_Linked()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var user = await harness.CreateUserAsync("self@access.test", AppRoles.Patient);
        var patient = harness.AddPatient();
        await harness.Linker.LinkUserToPatientAsync(user.Id, patient.Id);

        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = user.Id,
            Roles = [AppRoles.Patient],
            PatientId = patient.Id,
        };
        var currentPatient = new FakeCurrentPatient { HasLinkedPatient = true, PatientId = patient.Id };
        var sut = harness.CreatePatientService(currentUser, new FakeCurrentStaff(), currentPatient);

        var profile = await sut.GetCurrentPatientProfileAsync();
        profile.Id.Should().Be(patient.Id);
        profile.LinkedUserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task Cross_Patient_Access_Denied()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var user = await harness.CreateUserAsync("cross@access.test", AppRoles.Patient);
        var own = harness.AddPatient();
        var other = harness.AddPatient(firstName: "Other");
        await harness.Linker.LinkUserToPatientAsync(user.Id, own.Id);

        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = user.Id,
            Roles = [AppRoles.Patient],
            PatientId = own.Id,
        };
        var currentPatient = new FakeCurrentPatient { HasLinkedPatient = true, PatientId = own.Id };
        var sut = harness.CreatePatientService(currentUser, new FakeCurrentStaff(), currentPatient);

        var act = () => sut.GetPatientByIdAsync(other.Id);
        await act.Should().ThrowAsync<AuthorizationException>()
            .Where(e => e.ErrorCode == "authz.patient_self_scope_denied");
    }

    [Fact]
    public async Task Unlinked_Patient_Denied()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Patient],
        };
        var currentPatient = new FakeCurrentPatient { HasLinkedPatient = false };
        var sut = harness.CreatePatientService(currentUser, new FakeCurrentStaff(), currentPatient);

        var act = () => sut.GetCurrentPatientProfileAsync();
        await act.Should().ThrowAsync<AuthorizationException>()
            .Where(e => e.ErrorCode == "authz.missing_patient_linkage");
    }

    [Fact]
    public async Task Cross_Clinic_Staff_Access_Denied()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var (orgId, clinicA, clinicB) = await harness.SeedOrgWithTwoClinicsAsync();
        var patient = harness.AddPatient();
        harness.AddClinicPatient(clinicA, patient.Id, "A-1");

        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgId,
            ClinicId = clinicB,
            Role = AppRoles.Doctor,
        };
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
            OrganizationId = orgId,
            ClinicId = clinicB,
        };
        var sut = harness.CreatePatientService(currentUser, staff, new FakeCurrentPatient());

        var act = () => sut.GetPatientByIdAsync(patient.Id);
        await act.Should().ThrowAsync<AuthorizationException>()
            .Where(e => e.ErrorCode == "authz.clinic_access_denied");
    }

    [Fact]
    public async Task Cross_Organization_Staff_Access_Denied()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var (orgA, clinicA, _) = await harness.SeedOrgWithTwoClinicsAsync();
        var orgB = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        harness.Db.Organizations.Add(new Domain.Organizations.Organization
        {
            Id = orgB,
            Name = "Org B",
            Slug = "org-b-access",
            Status = Domain.Organizations.OrganizationStatus.Active,
        });
        harness.Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicB,
            OrganizationId = orgB,
            Name = "Clinic B Org",
            Slug = "clinic-b-org",
            IsActive = true,
        });
        await harness.Db.SaveChangesAsync();

        var patient = harness.AddPatient();
        harness.AddClinicPatient(clinicA, patient.Id, "A-1");

        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgB,
            ClinicId = clinicB,
            Role = AppRoles.OrganizationAdmin,
        };
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.OrganizationAdmin],
            OrganizationId = orgB,
            ClinicId = clinicB,
        };
        var sut = harness.CreatePatientService(currentUser, staff, new FakeCurrentPatient());

        var act = () => sut.GetPatientByIdAsync(patient.Id);
        await act.Should().ThrowAsync<AuthorizationException>()
            .Where(e => e.ErrorCode == "authz.organization_access_denied");
    }

    [Fact]
    public async Task Explicit_Platform_Admin_Bypass_Allowed()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var patient = harness.AddPatient();
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        var sut = harness.CreatePatientService(currentUser, new FakeCurrentStaff(), new FakeCurrentPatient());

        var profile = await sut.GetPatientByIdAsync(patient.Id, PlatformAdminBypass.Explicit);
        profile.Id.Should().Be(patient.Id);
    }

    [Fact]
    public async Task Client_Supplied_PatientId_Ignored_For_Current_Profile()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var user = await harness.CreateUserAsync("ignore@access.test", AppRoles.Patient);
        var linked = harness.AddPatient();
        var other = harness.AddPatient(firstName: "Other");
        await harness.Linker.LinkUserToPatientAsync(user.Id, linked.Id);

        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = user.Id,
            Roles = [AppRoles.Patient],
            PatientId = linked.Id,
        };
        var currentPatient = new FakeCurrentPatient { HasLinkedPatient = true, PatientId = linked.Id };
        var sut = harness.CreatePatientService(currentUser, new FakeCurrentStaff(), currentPatient);

        var profile = await sut.GetCurrentPatientProfileAsync();
        profile.Id.Should().Be(linked.Id);
        profile.Id.Should().NotBe(other.Id);
    }

    [Fact]
    public async Task Matching_Clinic_Staff_Access_Allowed()
    {
        await using var harness = await PatientTestHarness.CreateAsync();
        var (orgId, clinicA, _) = await harness.SeedOrgWithTwoClinicsAsync();
        var patient = harness.AddPatient();
        harness.AddClinicPatient(clinicA, patient.Id, "A-1");

        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgId,
            ClinicId = clinicA,
            Role = AppRoles.Doctor,
        };
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
            OrganizationId = orgId,
            ClinicId = clinicA,
        };
        var sut = harness.CreatePatientService(currentUser, staff, new FakeCurrentPatient());

        var profile = await sut.GetPatientByIdAsync(patient.Id);
        profile.Id.Should().Be(patient.Id);
    }
}

internal sealed class PatientTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private PatientTestHarness(ServiceProvider services, HealthCareDbContext db, IPatientAccountLinker linker)
    {
        _services = services;
        Db = db;
        Linker = linker;
    }

    public HealthCareDbContext Db { get; }

    public IPatientAccountLinker Linker { get; }

    public static async Task<PatientTestHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddLogging();
        services.AddDbContext<HealthCareDbContext>(options => options.UseInMemoryDatabase(dbName));
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>()
            .AddDefaultTokenProviders();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<HealthCareDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { AppRoles.Patient, AppRoles.Doctor, AppRoles.Receptionist, AppRoles.PlatformAdmin })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
            }
        }

        var linker = new PatientAccountLinker(
            db,
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            NullLogger<PatientAccountLinker>.Instance);

        return new PatientTestHarness(provider, db, linker);
    }

    public async Task<ApplicationUser> CreateUserAsync(string email, string role)
    {
        var userManager = _services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
        };
        var result = await userManager.CreateAsync(user, "ChangeMe_Test_1!");
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    public Patient AddPatient(string firstName = "Test", string lastName = "Patient")
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
        };
        Db.Patients.Add(patient);
        Db.SaveChanges();
        return patient;
    }

    public void AddClinicPatient(Guid clinicId, Guid patientId, string localNumber)
    {
        Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patientId,
            LocalPatientNumber = localNumber,
            Status = ClinicPatientStatus.Active,
        });
        Db.SaveChanges();
    }

    public async Task<(Guid OrgId, Guid ClinicA, Guid ClinicB)> SeedOrgWithTwoClinicsAsync()
    {
        var orgId = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        Db.Organizations.Add(new Domain.Organizations.Organization
        {
            Id = orgId,
            Name = "Org A",
            Slug = $"org-a-{orgId:N}"[..20],
            Status = Domain.Organizations.OrganizationStatus.Active,
        });
        Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicA,
            OrganizationId = orgId,
            Name = "Clinic A",
            Slug = $"clinic-a-{clinicA:N}"[..24],
            IsActive = true,
        });
        Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicB,
            OrganizationId = orgId,
            Name = "Clinic B",
            Slug = $"clinic-b-{clinicB:N}"[..24],
            IsActive = true,
        });
        await Db.SaveChangesAsync();
        return (orgId, clinicA, clinicB);
    }

    public PatientService CreatePatientService(
        FakeCurrentUser user,
        FakeCurrentStaff staff,
        FakeCurrentPatient patient)
    {
        var tenant = new Infrastructure.Authorization.TenantAccessService(
            user,
            staff,
            patient,
            NullLogger<Infrastructure.Authorization.TenantAccessService>.Instance);
        return new PatientService(
            Db,
            user,
            staff,
            patient,
            tenant,
            NullLogger<PatientService>.Instance);
    }

    public ValueTask DisposeAsync()
    {
        _services.Dispose();
        return ValueTask.CompletedTask;
    }
}
