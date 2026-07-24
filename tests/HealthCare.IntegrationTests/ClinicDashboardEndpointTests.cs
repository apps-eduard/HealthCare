using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class ClinicDashboardEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_clinic_dash_test")
            .WithUsername("healthcare")
            .WithPassword("healthcare_test")
            .Build();

        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var migrateDb = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>().UseNpgsql(_connectionString).Options);
        await migrateDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Anonymous_Returns_401()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/clinic/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/clinic/dashboard")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Organization_Admin_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");
        (await client.GetAsync("/api/v1/clinic/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clinic_Admin_Returns_Own_Clinic_Dashboard()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        var response = await client.GetAsync("/api/v1/clinic/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ClinicDashboardResponse>();
        body.Should().NotBeNull();
        body!.ClinicId.Should().NotBe(Guid.Empty);
        body.ClinicName.Should().NotBeNullOrWhiteSpace();
        body.OrganizationName.Should().NotBeNullOrWhiteSpace();
        body.DefaultTimeZoneId.Should().NotBeNullOrWhiteSpace();
        body.DashboardDate.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}$");
        body.TodayAppointmentsByStatus.Should().NotBeNull();
        body.OperationalWarnings.Should().NotBeNull();

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("MaxClinics");
        json.Should().NotContain("MaxStaff");
        json.Should().NotContain("RemainingClinic");
        json.ToLowerInvariant().Should().NotContain("billing");

        // Attempt to override clinic must fail.
        var otherClinic = Guid.NewGuid();
        var overrideResponse = await client.GetAsync($"/api/v1/clinic/dashboard?clinicId={otherClinic:D}");
        overrideResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_ClinicId()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        (await client.GetAsync("/api/v1/clinic/dashboard")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await client.GetAsync("/api/v1/clinic/dashboard?platformAdminBypass=true")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        Guid clinicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicId = await db.Clinics.AsNoTracking().Select(c => c.Id).FirstAsync();
        }

        var ok = await client.GetAsync(
            $"/api/v1/clinic/dashboard?platformAdminBypass=true&clinicId={clinicId:D}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<ClinicDashboardResponse>();
        body!.ClinicId.Should().Be(clinicId);

        var missing = await client.GetAsync(
            $"/api/v1/clinic/dashboard?platformAdminBypass=true&clinicId={Guid.NewGuid():D}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                IntegrationTestHost.ApplyDefaultSettings(builder);
                builder.UseEnvironment(Environments.Development);
                builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
                builder.UseSetting("Jwt:Issuer", "HealthCare");
                builder.UseSetting("Jwt:Audience", "HealthCare");
                builder.UseSetting("Jwt:SigningKey", "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+");
                builder.UseSetting("DevelopmentSeed:Admin:Email", "admin@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Admin:Password", "ChangeMe_Admin_1!");
                builder.UseSetting("DevelopmentSeed:Patient:Email", "patient@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:Password", "ChangeMe_Patient_1!");
                builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminEmail", "orgadmin@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminPassword", "ChangeMe_OrgAdmin_1!");
                builder.UseSetting("DevelopmentSeed:Patient:ClinicAdminEmail", "clinicadmin@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:ClinicAdminPassword", "ChangeMe_ClinicAdmin_1!");
                builder.UseSetting("Hangfire:Enabled", "false");
                builder.UseSetting("Hangfire:ScheduleRecurringJobs", "false");
                builder.UseSetting("Hangfire:Dashboard:Enabled", "false");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));
                    services.AddDbContext<HealthCareDbContext>(options => options.UseNpgsql(_connectionString));
                });
            });

    private static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
