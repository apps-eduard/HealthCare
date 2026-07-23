using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class PatientProfileAndClinicRegistrationEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string StaffEmail = "doctor.a@healthcare.local";
    private const string StaffPassword = "ChangeMe_DoctorA_1!";
    private const string ClinicCode = "dev-clinic-a";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_profile_test")
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
                builder.UseSetting("DevelopmentSeed:Admin:Email", "admin@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Admin:Password", "ChangeMe_Admin_1!");
                builder.UseSetting("DevelopmentSeed:Patient:Email", PatientEmail);
                builder.UseSetting("DevelopmentSeed:Patient:Password", PatientPassword);
                builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", StaffEmail);
                builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", StaffPassword);
                builder.UseSetting("DevelopmentSeed:Patient:ClinicSlug", ClinicCode);

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));
                    services.AddDbContext<HealthCareDbContext>(options => options.UseNpgsql(connectionString));
                });
            });

        _client = _factory.CreateClient();
        await _client.GetAsync("/health");
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
    public async Task Anonymous_Profile_Patch_Returns_401()
    {
        var response = await _client!.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/patients/me")
        {
            Content = JsonContent.Create(new { expectedVersion = 0, firstName = "X" }),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Staff_Profile_Patch_Returns_403()
    {
        await AuthenticateAsync(StaffEmail, StaffPassword);
        var response = await _client!.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/patients/me")
        {
            Content = JsonContent.Create(new { expectedVersion = 0, firstName = "X" }),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Linked_Patient_Can_Update_Profile_And_See_Changes()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var me = await client.GetFromJsonAsync<PatientProfileResponse>("/api/v1/patients/me");

        var patch = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/patients/me")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = me!.Version,
                firstName = "Updated",
                mobileNumber = "+15551212",
            }),
        });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<PatientProfileResponse>();
        updated!.FirstName.Should().Be("Updated");
        updated.MobileNumber.Should().Be("+15551212");
        updated.Version.Should().Be(me.Version + 1);

        var again = await client.GetFromJsonAsync<PatientProfileResponse>("/api/v1/patients/me");
        again!.FirstName.Should().Be("Updated");
        again.Version.Should().Be(updated.Version);
    }

    [Fact]
    public async Task Empty_Patch_Returns_Validation_Error()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var me = await client.GetFromJsonAsync<PatientProfileResponse>("/api/v1/patients/me");
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/patients/me")
        {
            Content = JsonContent.Create(new { expectedVersion = me!.Version }),
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Concurrency_Mismatch_Returns_Conflict()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var me = await client.GetFromJsonAsync<PatientProfileResponse>("/api/v1/patients/me");
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/v1/patients/me")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = me!.Version - 1,
                firstName = "Stale",
            }),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Patient_Registers_With_Clinic_Code_Idempotently()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var first = await client.PostAsJsonAsync("/api/v1/patients/me/clinics/register", new RegisterPatientWithClinicRequest
        {
            ClinicCode = ClinicCode,
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<ClinicPatientEnrollmentResponse>();

        var second = await client.PostAsJsonAsync("/api/v1/patients/me/clinics/register", new RegisterPatientWithClinicRequest
        {
            ClinicCode = ClinicCode,
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<ClinicPatientEnrollmentResponse>();
        secondBody!.AlreadyEnrolled.Should().BeTrue();
        secondBody.LocalPatientNumber.Should().Be(firstBody!.LocalPatientNumber);
    }

    [Fact]
    public async Task Invalid_Clinic_Code_Does_Not_Reveal_Details()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await client.PostAsJsonAsync("/api/v1/patients/me/clinics/register", new RegisterPatientWithClinicRequest
        {
            ClinicCode = "unknown-clinic",
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Organization");
        body.Should().Contain("clinic_code_invalid");
    }

    private async Task AuthenticateAsync(string email, string password)
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
