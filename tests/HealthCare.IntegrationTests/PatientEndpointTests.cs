using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class PatientEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient.int@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string UnlinkedEmail = "unlinked.int@healthcare.local";
    private const string UnlinkedPassword = "ChangeMe_Unlinked_1!";
    private const string StaffAEmail = "doctor.a.int@healthcare.local";
    private const string StaffAPassword = "ChangeMe_DoctorA_1!";
    private const string StaffBEmail = "doctor.b.int@healthcare.local";
    private const string StaffBPassword = "ChangeMe_DoctorB_1!";
    private const string AdminEmail = "admin@healthcare.local";
    private const string AdminPassword = "ChangeMe_Admin_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private Guid _linkedPatientId;
    private Guid _otherPatientId;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_patient_test")
            .WithUsername("healthcare")
            .WithPassword("healthcare_test")
            .Build();

        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        var migrateOptions = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var migrateDb = new HealthCareDbContext(migrateOptions))
        {
            await migrateDb.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
                builder.UseSetting("Jwt:Issuer", "HealthCare");
                builder.UseSetting("Jwt:Audience", "HealthCare");
                builder.UseSetting("Jwt:SigningKey", "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+");
                builder.UseSetting("DevelopmentSeed:Admin:Email", AdminEmail);
                builder.UseSetting("DevelopmentSeed:Admin:Password", AdminPassword);
                builder.UseSetting("DevelopmentSeed:Patient:Email", "");
                builder.UseSetting("DevelopmentSeed:Patient:Password", "");

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));
                    services.AddDbContext<HealthCareDbContext>(options => options.UseNpgsql(connectionString));
                });
            });

        _client = _factory.CreateClient();
        await SeedScenarioAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Anonymous_Patients_Me_Returns_401()
    {
        var response = await _client!.GetAsync("/api/v1/patients/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Staff_Cannot_Call_Patients_Me()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/patients/me");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Linked_Patient_Receives_Own_Profile()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/patients/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<PatientProfileResponse>();
        profile!.Id.Should().Be(_linkedPatientId);
        profile.FirstName.Should().Be("Linked");
    }

    [Fact]
    public async Task Unlinked_Patient_Is_Denied()
    {
        await AuthenticateAsync(UnlinkedEmail, UnlinkedPassword);
        var response = await _client!.GetAsync("/api/v1/patients/me");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patient_Cannot_Access_Another_Patient()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync($"/api/v1/patients/{_otherPatientId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Client_Supplied_PatientId_Cannot_Override_Linked_Patient()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var me = await _client!.GetAsync($"/api/v1/patients/me?patientId={_otherPatientId}");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await me.Content.ReadFromJsonAsync<PatientProfileResponse>();
        profile!.Id.Should().Be(_linkedPatientId);
        profile.Id.Should().NotBe(_otherPatientId);
    }

    [Fact]
    public async Task Clinic_A_Staff_Can_Access_Clinic_A_Patient_Clinic_B_Cannot()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var allowed = await _client!.GetAsync($"/api/v1/patients/{_linkedPatientId}");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        var denied = await _client!.GetAsync($"/api/v1/patients/{_linkedPatientId}");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auth_Me_Returns_Linked_PatientId()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        me!.PatientId.Should().Be(_linkedPatientId);
        me.HasLinkedPatient.Should().BeTrue();
        me.Roles.Should().Contain(AppRoles.Patient);
    }

    private async Task AuthenticateAsync(string email, string password)
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }

    private async Task SeedScenarioAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var orgA = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Org A",
            Slug = "int-org-a",
            Status = OrganizationStatus.Active,
        };
        var orgB = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Org B",
            Slug = "int-org-b",
            Status = OrganizationStatus.Active,
        };
        var clinicA = new Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgA.Id,
            Name = "Clinic A",
            Slug = "int-clinic-a",
            IsActive = true,
        };
        var clinicB = new Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgB.Id,
            Name = "Clinic B",
            Slug = "int-clinic-b",
            IsActive = true,
        };
        db.Organizations.AddRange(orgA, orgB);
        db.Clinics.AddRange(clinicA, clinicB);

        var linkedPatient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Linked",
            LastName = "Patient",
            IsActive = true,
        };
        var otherPatient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Other",
            LastName = "Patient",
            IsActive = true,
        };
        db.Patients.AddRange(linkedPatient, otherPatient);
        db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicA.Id,
            PatientId = linkedPatient.Id,
            LocalPatientNumber = "INT-A-1",
            Status = ClinicPatientStatus.Active,
        });
        await db.SaveChangesAsync();

        _linkedPatientId = linkedPatient.Id;
        _otherPatientId = otherPatient.Id;

        var patientUser = await CreateUserAsync(userManager, PatientEmail, PatientPassword, AppRoles.Patient);
        linkedPatient.UserId = patientUser.Id;
        await db.SaveChangesAsync();

        await CreateUserAsync(userManager, UnlinkedEmail, UnlinkedPassword, AppRoles.Patient);

        var staffA = await CreateUserAsync(userManager, StaffAEmail, StaffAPassword, AppRoles.Doctor);
        var staffB = await CreateUserAsync(userManager, StaffBEmail, StaffBPassword, AppRoles.Doctor);
        db.StaffMembers.AddRange(
            new StaffMember
            {
                Id = Guid.NewGuid(),
                UserId = staffA.Id,
                OrganizationId = orgA.Id,
                ClinicId = clinicA.Id,
                Role = AppRoles.Doctor,
                IsActive = true,
            },
            new StaffMember
            {
                Id = Guid.NewGuid(),
                UserId = staffB.Id,
                OrganizationId = orgB.Id,
                ClinicId = clinicB.Id,
                Role = AppRoles.Doctor,
                IsActive = true,
            });
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string role)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
        };
        var create = await userManager.CreateAsync(user, password);
        create.Succeeded.Should().BeTrue(string.Join("; ", create.Errors.Select(e => e.Description)));
        var roleResult = await userManager.AddToRoleAsync(user, role);
        roleResult.Succeeded.Should().BeTrue();
        return user;
    }
}
