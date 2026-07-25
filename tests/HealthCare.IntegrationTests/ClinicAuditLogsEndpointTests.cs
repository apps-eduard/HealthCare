using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Identity;
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

public sealed class ClinicAuditLogsEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_clinic_audit_test")
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
        (await client.GetAsync("/api/v1/clinic/audit-logs")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/clinic/audit-logs")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Organization_Admin_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");
        (await client.GetAsync("/api/v1/clinic/audit-logs")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clinic_Admin_Sees_Own_Clinic_Allowlisted_Events_Only()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        Guid clinicId;
        Guid otherClinicId;
        Guid allowedId;
        Guid otherClinicEventId;
        Guid nonAllowlistedId;
        string from;
        string to;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var meStaff = await db.StaffMembers.AsNoTracking()
                .SingleAsync(s => s.Role == AppRoles.ClinicAdmin && s.IsActive);
            clinicId = meStaff.ClinicId;
            otherClinicId = await db.Clinics.AsNoTracking()
                .Where(c => c.Id != clinicId)
                .Select(c => c.Id)
                .FirstAsync();
            var orgId = meStaff.OrganizationId;
            var now = DateTimeOffset.UtcNow;
            from = now.AddDays(-7).UtcDateTime.ToString("yyyy-MM-dd");
            to = now.UtcDateTime.ToString("yyyy-MM-dd");

            allowedId = Guid.NewGuid();
            otherClinicEventId = Guid.NewGuid();
            nonAllowlistedId = Guid.NewGuid();

            db.OrganizationAuditEvents.AddRange(
                new OrganizationAuditEvent
                {
                    Id = allowedId,
                    OrganizationId = orgId,
                    ClinicId = clinicId,
                    ActorUserId = meStaff.UserId,
                    Category = "clinic",
                    Action = "clinic_profile_update",
                    ResultCode = "succeeded",
                    ResourceType = "clinic",
                    ResourceId = clinicId,
                    CorrelationId = "ca9-allowed",
                    OccurredAtUtc = now.AddMinutes(-5),
                },
                new OrganizationAuditEvent
                {
                    Id = otherClinicEventId,
                    OrganizationId = orgId,
                    ClinicId = otherClinicId,
                    ActorUserId = meStaff.UserId,
                    Category = "clinic",
                    Action = "clinic_profile_update",
                    ResultCode = "succeeded",
                    CorrelationId = "ca9-other-clinic",
                    OccurredAtUtc = now.AddMinutes(-4),
                },
                new OrganizationAuditEvent
                {
                    Id = nonAllowlistedId,
                    OrganizationId = orgId,
                    ClinicId = clinicId,
                    ActorUserId = meStaff.UserId,
                    Category = "security",
                    Action = "organization_profile_update",
                    ResultCode = "succeeded",
                    CorrelationId = "ca9-non-allowlisted",
                    OccurredAtUtc = now.AddMinutes(-3),
                });
            await db.SaveChangesAsync();
        }

        var qs = $"fromDate={from}&toDate={to}";
        var response = await client.GetAsync($"/api/v1/clinic/audit-logs?{qs}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ClinicAuditLogListResponse>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        body.Should().NotBeNull();
        body!.ClinicId.Should().Be(clinicId);
        body.Items.Should().Contain(i => i.AuditLogId == allowedId && i.Action == "clinic_profile_update");
        body.Items.Should().NotContain(i => i.AuditLogId == otherClinicEventId);
        body.Items.Should().NotContain(i => i.AuditLogId == nonAllowlistedId);

        AssertSafeJson(json);

        var detail = await client.GetAsync($"/api/v1/clinic/audit-logs/{allowedId:D}?{qs}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertSafeJson(await detail.Content.ReadAsStringAsync());

        (await client.GetAsync($"/api/v1/clinic/audit-logs?clinicId={otherClinicId:D}&{qs}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/clinic/audit-logs?fromDate=2026-01-01&toDate=2026-04-05"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/clinic/audit-logs/export")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_ClinicId()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        (await client.GetAsync("/api/v1/clinic/audit-logs")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await client.GetAsync("/api/v1/clinic/audit-logs?platformAdminBypass=true")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        Guid clinicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicId = await db.Clinics.AsNoTracking().Select(c => c.Id).FirstAsync();
            var orgId = await db.Clinics.AsNoTracking()
                .Where(c => c.Id == clinicId)
                .Select(c => c.OrganizationId)
                .SingleAsync();
            db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                ClinicId = clinicId,
                Category = "clinic",
                Action = "staff_created",
                ResultCode = "succeeded",
                OccurredAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var ok = await client.GetAsync(
            $"/api/v1/clinic/audit-logs?platformAdminBypass=true&clinicId={clinicId:D}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<ClinicAuditLogListResponse>();
        body!.ClinicId.Should().Be(clinicId);
        AssertSafeJson(await ok.Content.ReadAsStringAsync());

        var missing = await client.GetAsync(
            $"/api/v1/clinic/audit-logs?platformAdminBypass=true&clinicId={Guid.NewGuid():D}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static void AssertSafeJson(string json)
    {
        json.Should().NotContain("Metadata");
        json.Should().NotContain("Password");
        json.Should().NotContain("Token");
        json.Should().NotContain("MedicalNote");
        json.ToLowerInvariant().Should().NotContain("billing");
        json.ToLowerInvariant().Should().NotContain("subscription");
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
