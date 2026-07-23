using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Application.Identity;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
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

public sealed class PatientRegistrationEndpointTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@healthcare.local";
    private const string AdminPassword = "ChangeMe_Admin_1!";
    private const string StaffEmail = "doctor.reg@healthcare.local";
    private const string StaffPassword = "ChangeMe_DoctorA_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_registration_test")
            .WithUsername("healthcare")
            .WithPassword("healthcare_test")
            .Build();

        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await using (var migrateDb = new HealthCareDbContext(
                         new DbContextOptionsBuilder<HealthCareDbContext>().UseNpgsql(connectionString).Options))
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
                builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", StaffEmail);
                builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", StaffPassword);

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));
                    services.AddDbContext<HealthCareDbContext>(options => options.UseNpgsql(connectionString));
                });
            });

        _client = _factory.CreateClient();
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
    public async Task Registration_Creates_User_And_Patient_Atomically_And_Blocks_Login_Until_Confirm()
    {
        var client = _client!;
        var email = $"new-{Guid.NewGuid():N}@test.local";
        var register = await client.PostAsJsonAsync("/api/v1/auth/register/patient", new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "Reg",
            LastName = "Patient",
        });
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            user.Should().NotBeNull();
            user!.EmailConfirmed.Should().BeFalse();
            (await users.IsInRoleAsync(user, AppRoles.Patient)).Should().BeTrue();
            (await db.Patients.CountAsync(p => p.UserId == user.Id)).Should().Be(1);
        }

        var loginBefore = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
        });
        loginBefore.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var tokenResponse = await client.GetAsync($"/api/v1/auth/dev/confirmation-token?email={Uri.EscapeDataString(email)}");
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var captured = await tokenResponse.Content.ReadFromJsonAsync<DevTokenDto>();

        var confirm = await client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest
        {
            Email = email,
            Token = captured!.Token,
        });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginAfter = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
        });
        loginAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await loginAfter.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me");
        me!.PatientId.Should().NotBeNull();
        me.Roles.Should().Contain(AppRoles.Patient);
    }

    [Fact]
    public async Task Duplicate_Registration_Is_Safe()
    {
        var email = $"dup-{Guid.NewGuid():N}@test.local";
        var body = new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "Dup",
            LastName = "Patient",
        };

        (await _client!.PostAsJsonAsync("/api/v1/auth/register/patient", body)).EnsureSuccessStatusCode();
        var second = await _client!.PostAsJsonAsync("/api/v1/auth/register/patient", body);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory!.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.Users.CountAsync(u => u.Email == email)).Should().Be(1);
    }

    [Fact]
    public async Task Clinic_Enrollment_Is_Idempotent()
    {
        // Ensure development staff/clinic seed exists by hitting health then using seeded staff from options.
        // Seed patient via registration + confirm, then enroll with staff from DevelopmentPatientSeeder clinic.
        await _client!.GetAsync("/health");

        Guid clinicId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            // Force seed by resolving hosted startup already ran; create clinic if missing from empty patient seed.
            var clinic = await db.Clinics.FirstOrDefaultAsync(c => c.Slug == "dev-clinic-a");
            if (clinic is null)
            {
                // Patient seed skipped when email empty; create org/clinic/staff manually.
                var org = new Domain.Organizations.Organization
                {
                    Id = Guid.NewGuid(),
                    Name = "Reg Org",
                    Slug = "reg-org",
                    Status = Domain.Organizations.OrganizationStatus.Active,
                };
                clinic = new Domain.Clinics.Clinic
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = org.Id,
                    Name = "Reg Clinic",
                    Slug = "dev-clinic-a",
                    IsActive = true,
                };
                db.Organizations.Add(org);
                db.Clinics.Add(clinic);
                await db.SaveChangesAsync();

                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var staffUser = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    Email = StaffEmail,
                    UserName = StaffEmail,
                    EmailConfirmed = true,
                    IsActive = true,
                };
                await users.CreateAsync(staffUser, StaffPassword);
                await users.AddToRoleAsync(staffUser, AppRoles.Doctor);
                db.StaffMembers.Add(new Domain.Staff.StaffMember
                {
                    Id = Guid.NewGuid(),
                    UserId = staffUser.Id,
                    OrganizationId = org.Id,
                    ClinicId = clinic.Id,
                    Role = AppRoles.Doctor,
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            clinicId = clinic.Id;
        }

        var email = $"enroll-{Guid.NewGuid():N}@test.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register/patient", new PatientRegisterRequest
        {
            Email = email,
            Password = "ChangeMe_Patient_1!",
            ConfirmPassword = "ChangeMe_Patient_1!",
            FirstName = "En",
            LastName = "Roll",
        });
        var tokenResponse = await _client.GetFromJsonAsync<DevTokenDto>(
            $"/api/v1/auth/dev/confirmation-token?email={Uri.EscapeDataString(email)}");
        await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", new ConfirmEmailRequest
        {
            Email = email,
            Token = tokenResponse!.Token,
        });

        Guid patientId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            patientId = await db.Patients.Where(p => p.UserId == user!.Id).Select(p => p.Id).SingleAsync();
        }

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = StaffEmail,
            Password = StaffPassword,
        });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var first = await _client.PostAsync($"/api/v1/clinics/{clinicId}/patients/{patientId}/enroll", null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<ClinicPatientEnrollmentResponse>();

        var second = await _client.PostAsync($"/api/v1/clinics/{clinicId}/patients/{patientId}/enroll", null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<ClinicPatientEnrollmentResponse>();

        secondBody!.AlreadyEnrolled.Should().BeTrue();
        secondBody.LocalPatientNumber.Should().Be(firstBody!.LocalPatientNumber);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            (await db.ClinicPatients.CountAsync(cp => cp.ClinicId == clinicId && cp.PatientId == patientId))
                .Should().Be(1);
        }
    }

    private sealed class DevTokenDto
    {
        public string Email { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;
    }
}
