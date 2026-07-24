using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class OrganizationSettingsEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_org_settings_test")
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
    public async Task Anonymous_Settings_Returns_401()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/organization/settings")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Settings_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/organization/settings")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Clinic_Admin_Settings_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");
        (await client.GetAsync("/api/v1/organization/settings")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Organization_Admin_Can_Read_And_Update()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");

        var current = await client.GetFromJsonAsync<OrganizationSettingsResponse>("/api/v1/organization/settings");
        current.Should().NotBeNull();
        current!.Name.Should().NotBeNullOrWhiteSpace();
        current.Slug.Should().NotBeNullOrWhiteSpace();
        current.Status.Should().Be(nameof(OrganizationStatus.Active));
        current.MaxClinics.Should().BeGreaterThan(0);
        current.MaxStaff.Should().BeGreaterThan(0);

        var patch = await client.PatchAsJsonAsync(
            "/api/v1/organization/settings",
            new UpdateOrganizationSettingsRequest
            {
                ExpectedVersion = current.Version,
                ContactEmail = "org-profile@healthcare.local",
                Country = "SA",
                DefaultTimeZoneId = "Asia/Riyadh",
                BrandingPlaceholder = "Demo Org",
            });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<OrganizationSettingsResponse>();
        updated.Should().NotBeNull();
        updated!.ContactEmail.Should().Be("org-profile@healthcare.local");
        updated.Country.Should().Be("SA");
        updated.DefaultTimeZoneId.Should().Be("Asia/Riyadh");
        updated.BrandingPlaceholder.Should().Be("Demo Org");
        updated.Version.Should().Be(current.Version + 1);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var audit = await db.OrganizationAuditEvents
                .Where(e => e.OrganizationId == updated.OrganizationId
                            && e.Action == "organization_profile_update")
                .OrderByDescending(e => e.OccurredAtUtc)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
            audit!.Category.Should().Be("organization");
            audit.ResultCode.Should().Be("succeeded");
        }
    }

    [Fact]
    public async Task Concurrency_Conflict_Returns_409()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");

        var current = await client.GetFromJsonAsync<OrganizationSettingsResponse>("/api/v1/organization/settings");
        current.Should().NotBeNull();

        var conflict = await client.PatchAsJsonAsync(
            "/api/v1/organization/settings",
            new UpdateOrganizationSettingsRequest
            {
                ExpectedVersion = current!.Version + 50,
                ContactPhone = "+10000000000",
            });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problemJson = await conflict.Content.ReadAsStringAsync();
        problemJson.Should().Contain(OrganizationSettingsErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Cross_Organization_Override_Is_Denied()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");

        var foreignOrgId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var foreign = new Organization
            {
                Id = foreignOrgId,
                Name = "Foreign Org Settings",
                Slug = "foreign-org-settings-" + Guid.NewGuid().ToString("N")[..8],
                Status = OrganizationStatus.Active,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Organizations.Add(foreign);
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/v1/organization/settings?organizationId={foreignOrgId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_Selected_Organization()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        (await client.GetAsync("/api/v1/organization/settings")).StatusCode
            .Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);

        Guid orgId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            orgId = await db.Organizations.Select(o => o.Id).FirstAsync();
        }

        (await client.GetAsync($"/api/v1/organization/settings?organizationId={orgId:D}")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);

        var ok = await client.GetAsync(
            $"/api/v1/organization/settings?organizationId={orgId:D}&platformAdminBypass=true");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<OrganizationSettingsResponse>();
        body!.OrganizationId.Should().Be(orgId);
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
                builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", "doctor.a@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", "ChangeMe_DoctorA_1!");
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
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
