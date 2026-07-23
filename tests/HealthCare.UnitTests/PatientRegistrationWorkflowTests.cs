using FluentValidation.TestHelper;
using FluentAssertions;
using HealthCare.Application.Identity;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class PatientRegistrationServiceTests
{
    [Fact]
    public async Task Successful_Registration_Assigns_Patient_Role_And_Links_Patient()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync();
        var email = $"reg-{Guid.NewGuid():N}@test.local";

        var response = await harness.Registration.RegisterAsync(new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "Ada",
            LastName = "Lovelace",
        });

        response.RequiresEmailConfirmation.Should().BeTrue();

        var user = await harness.UserManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        user!.EmailConfirmed.Should().BeFalse();
        (await harness.UserManager.IsInRoleAsync(user, AppRoles.Patient)).Should().BeTrue();

        var patient = await harness.Db.Patients.SingleAsync(p => p.UserId == user.Id);
        patient.FirstName.Should().Be("Ada");
        harness.TokenStore.TryGet(email, out var token).Should().BeTrue();
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Duplicate_Email_Returns_Generic_Response_Without_Second_User()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync();
        var email = $"dup-{Guid.NewGuid():N}@test.local";
        var request = new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "One",
            LastName = "Patient",
        };

        await harness.Registration.RegisterAsync(request);
        var second = await harness.Registration.RegisterAsync(request);

        second.Message.Should().Contain("confirmation");
        (await harness.UserManager.Users.CountAsync(u => u.Email == email)).Should().Be(1);
        (await harness.Db.Patients.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Transaction_Rollback_On_Role_Failure_Leaves_No_Patient()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync(seedPatientRole: false);
        var email = $"fail-{Guid.NewGuid():N}@test.local";

        var act = () => harness.Registration.RegisterAsync(new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "Fail",
            LastName = "Case",
        });

        await act.Should().ThrowAsync<AuthenticationException>();
        (await harness.Db.Patients.CountAsync()).Should().Be(0);
        // User may be deleted on non-relational rollback path.
        var user = await harness.UserManager.FindByEmailAsync(email);
        user.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_Confirmation_Token_Fails()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync();
        var email = $"badtok-{Guid.NewGuid():N}@test.local";
        await harness.Registration.RegisterAsync(new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "Tok",
            LastName = "Fail",
        });

        var act = () => harness.Registration.ConfirmEmailAsync(new ConfirmEmailRequest
        {
            Email = email,
            Token = "not-a-valid-token",
        });

        await act.Should().ThrowAsync<AuthenticationException>()
            .Where(e => e.ErrorCode == AuthErrorCodes.InvalidConfirmationToken);
    }

    [Fact]
    public async Task Already_Confirmed_Account_Is_Idempotent()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync();
        var email = $"ok-{Guid.NewGuid():N}@test.local";
        await harness.Registration.RegisterAsync(new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "Ok",
            LastName = "Confirm",
        });
        harness.TokenStore.TryGet(email, out var token).Should().BeTrue();

        var first = await harness.Registration.ConfirmEmailAsync(new ConfirmEmailRequest
        {
            Email = email,
            Token = token!,
        });
        first.EmailConfirmed.Should().BeTrue();

        var second = await harness.Registration.ConfirmEmailAsync(new ConfirmEmailRequest
        {
            Email = email,
            Token = "anything",
        });
        second.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void Password_Validation_Rejects_Weak_Passwords()
    {
        var validator = new PatientRegisterRequestValidator();
        var result = validator.TestValidate(new PatientRegisterRequest
        {
            Email = "a@b.com",
            Password = "weak",
            ConfirmPassword = "weak",
            FirstName = "A",
            LastName = "B",
        });
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Client_Supplied_Scope_Fields_Are_Not_On_Register_Contract()
    {
        typeof(PatientRegisterRequest).GetProperty("PatientId").Should().BeNull();
        typeof(PatientRegisterRequest).GetProperty("OrganizationId").Should().BeNull();
        typeof(PatientRegisterRequest).GetProperty("ClinicId").Should().BeNull();
        typeof(PatientRegisterRequest).GetProperty("Role").Should().BeNull();
        typeof(PatientRegisterRequest).GetProperty("ApplicationUserId").Should().BeNull();
    }
}

public sealed class ClinicEnrollmentServiceTests
{
    [Fact]
    public async Task Enrollment_Is_Idempotent_And_Keeps_Local_Number()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync();
        var (orgId, clinicId) = await harness.SeedClinicAsync();
        var patient = harness.AddUnlinkedPatient();

        var staffUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
            OrganizationId = orgId,
            ClinicId = clinicId,
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgId,
            ClinicId = clinicId,
            Role = AppRoles.Doctor,
        };

        var sut = harness.CreateEnrollment(staffUser, staff);
        var first = await sut.EnrollAsync(clinicId, patient.Id);
        var second = await sut.EnrollAsync(clinicId, patient.Id);

        first.AlreadyEnrolled.Should().BeFalse();
        second.AlreadyEnrolled.Should().BeTrue();
        second.LocalPatientNumber.Should().Be(first.LocalPatientNumber);
        second.LocalPatientNumber.Should().MatchRegex(@"^P-\d{6}$");
        (await harness.Db.ClinicPatients.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_Clinic_Enrollment_Is_Denied()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync();
        var (_, clinicA) = await harness.SeedClinicAsync("slug-a");
        var (orgB, clinicB) = await harness.SeedClinicAsync("slug-b");
        var patient = harness.AddUnlinkedPatient();

        var staffUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
            OrganizationId = orgB,
            ClinicId = clinicB,
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgB,
            ClinicId = clinicB,
            Role = AppRoles.Doctor,
        };

        var sut = harness.CreateEnrollment(staffUser, staff);
        var act = () => sut.EnrollAsync(clinicA, patient.Id);
        await act.Should().ThrowAsync<Application.Authorization.AuthorizationException>();
    }

    [Fact]
    public async Task Local_Patient_Numbers_Are_Unique_Per_Clinic()
    {
        await using var harness = await RegistrationTestHarness.CreateAsync();
        var (orgId, clinicId) = await harness.SeedClinicAsync();
        var p1 = harness.AddUnlinkedPatient("One");
        var p2 = harness.AddUnlinkedPatient("Two");

        var staffUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.Doctor],
            OrganizationId = orgId,
            ClinicId = clinicId,
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = orgId,
            ClinicId = clinicId,
            Role = AppRoles.Doctor,
        };
        var sut = harness.CreateEnrollment(staffUser, staff);

        var a = await sut.EnrollAsync(clinicId, p1.Id);
        var b = await sut.EnrollAsync(clinicId, p2.Id);
        a.LocalPatientNumber.Should().NotBe(b.LocalPatientNumber);
    }
}

internal sealed class RegistrationTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private RegistrationTestHarness(
        ServiceProvider services,
        HealthCareDbContext db,
        UserManager<ApplicationUser> userManager,
        IPatientRegistrationService registration,
        IDevelopmentConfirmationTokenStore tokenStore)
    {
        _services = services;
        Db = db;
        UserManager = userManager;
        Registration = registration;
        TokenStore = tokenStore;
    }

    public HealthCareDbContext Db { get; }

    public UserManager<ApplicationUser> UserManager { get; }

    public IPatientRegistrationService Registration { get; }

    public IDevelopmentConfirmationTokenStore TokenStore { get; }

    public static async Task<RegistrationTestHarness> CreateAsync(bool seedPatientRole = true)
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString("N");
        services.AddLogging();
        services.AddDbContext<HealthCareDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>()
            .AddDefaultTokenProviders();
        services.AddSingleton<IDevelopmentConfirmationTokenStore, DevelopmentConfirmationTokenStore>();
        services.AddScoped<IAccountEmailSender, DevelopmentAccountEmailSender>();
        services.AddScoped<IPatientRegistrationService, PatientRegistrationService>();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<HealthCareDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (seedPatientRole)
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(AppRoles.Patient) { Id = Guid.NewGuid() });
        }

        foreach (var role in new[] { AppRoles.Doctor })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
            }
        }

        return new RegistrationTestHarness(
            provider,
            db,
            provider.GetRequiredService<UserManager<ApplicationUser>>(),
            provider.GetRequiredService<IPatientRegistrationService>(),
            provider.GetRequiredService<IDevelopmentConfirmationTokenStore>());
    }

    public async Task<(Guid OrgId, Guid ClinicId)> SeedClinicAsync(string slugSuffix = "main")
    {
        var orgId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        Db.Organizations.Add(new Domain.Organizations.Organization
        {
            Id = orgId,
            Name = "Org",
            Slug = $"org-{slugSuffix}-{orgId:N}"[..24],
            Status = Domain.Organizations.OrganizationStatus.Active,
        });
        Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicId,
            OrganizationId = orgId,
            Name = "Clinic",
            Slug = $"clinic-{slugSuffix}-{clinicId:N}"[..28],
            IsActive = true,
        });
        await Db.SaveChangesAsync();
        return (orgId, clinicId);
    }

    public Patient AddUnlinkedPatient(string firstName = "Pat")
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = "Ent",
            IsActive = true,
        };
        Db.Patients.Add(patient);
        Db.SaveChanges();
        return patient;
    }

    public ClinicEnrollmentService CreateEnrollment(FakeCurrentUser user, FakeCurrentStaff staff)
    {
        var tenant = new Infrastructure.Authorization.TenantAccessService(
            user,
            staff,
            new FakeCurrentPatient(),
            NullLogger<Infrastructure.Authorization.TenantAccessService>.Instance);
        var numbers = new LocalPatientNumberGenerator(Db, NullLogger<LocalPatientNumberGenerator>.Instance);
        return new ClinicEnrollmentService(
            Db,
            tenant,
            numbers,
            NullLogger<ClinicEnrollmentService>.Instance);
    }

    public ValueTask DisposeAsync()
    {
        _services.Dispose();
        return ValueTask.CompletedTask;
    }
}
