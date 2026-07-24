using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed class ClinicSettingsEndpointTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_clinic_settings_test")
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
        (await client.GetAsync("/api/v1/clinic/settings")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Doctor_And_Org_Admin_Are_Denied()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/clinic/settings")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);

        await AuthenticateAsync(client, "doctor.a@healthcare.local", "ChangeMe_DoctorA_1!");
        (await client.GetAsync("/api/v1/clinic/settings")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");
        (await client.GetAsync("/api/v1/clinic/settings")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clinic_Admin_Can_Read_Patch_And_Persist_Without_Mutating_Read_Only_Fields()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        var current = await client.GetFromJsonAsync<ClinicSettingsResponse>("/api/v1/clinic/settings");
        current.Should().NotBeNull();
        current!.ClinicId.Should().NotBe(Guid.Empty);
        current.OrganizationName.Should().NotBeNullOrWhiteSpace();
        current.Slug.Should().NotBeNullOrWhiteSpace();
        var originalSlug = current.Slug;
        var originalOrgId = current.OrganizationId;
        var originalActive = current.IsActive;

        var patch = await client.PatchAsJsonAsync(
            "/api/v1/clinic/settings",
            new UpdateClinicSettingsRequest
            {
                ExpectedVersion = current.Version,
                Name = "Clinic Profile Updated",
                Specialty = "Family Medicine",
                ContactEmail = "clinic-profile@healthcare.local",
                ContactPhone = "+966511111111",
                Address = "Updated Address 1",
                City = "Riyadh",
                Country = "SA",
                DefaultTimeZoneId = "Asia/Riyadh",
            },
            PatchJsonOptions);
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<ClinicSettingsResponse>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Clinic Profile Updated");
        updated.Specialty.Should().Be("Family Medicine");
        updated.ContactEmail.Should().Be("clinic-profile@healthcare.local");
        updated.Address.Should().Be("Updated Address 1");
        updated.Version.Should().Be(current.Version + 1);
        updated.Slug.Should().Be(originalSlug);
        updated.OrganizationId.Should().Be(originalOrgId);
        updated.IsActive.Should().Be(originalActive);

        var reloaded = await client.GetFromJsonAsync<ClinicSettingsResponse>("/api/v1/clinic/settings");
        reloaded!.Name.Should().Be("Clinic Profile Updated");
        reloaded.ContactEmail.Should().Be("clinic-profile@healthcare.local");

        // Read-only fields cannot be changed via body (ignored / not in contract).
        var body = await patch.Content.ReadAsStringAsync();
        body.Should().NotContain("MaxClinics");
        body.ToLowerInvariant().Should().NotContain("billing");
        body.ToLowerInvariant().Should().NotContain("subscription");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var audit = await db.OrganizationAuditEvents
                .Where(e => e.ClinicId == updated.ClinicId && e.Action == "clinic_profile_update")
                .OrderByDescending(e => e.OccurredAtUtc)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
            audit!.Category.Should().Be("clinic");
            audit.ResultCode.Should().Be("succeeded");
            audit.OrganizationId.Should().Be(updated.OrganizationId);
            var auditJson = JsonSerializer.Serialize(audit);
            auditJson.Should().NotContain("clinic-profile@healthcare.local");
            auditJson.Should().NotContain("+966511111111");
            auditJson.Should().NotContain("ChangeMe_");
        }
    }

    [Fact]
    public async Task Other_Clinic_And_Mismatched_ClinicId_Are_Rejected()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        Guid otherClinicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var own = await client.GetFromJsonAsync<ClinicSettingsResponse>("/api/v1/clinic/settings");
            otherClinicId = await db.Clinics.AsNoTracking()
                .Where(c => c.Id != own!.ClinicId)
                .Select(c => c.Id)
                .FirstAsync();
        }

        (await client.GetAsync($"/api/v1/clinic/settings?clinicId={otherClinicId:D}")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_Explicit_ClinicId()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        (await client.GetAsync("/api/v1/clinic/settings")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);

        Guid clinicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicId = await db.Clinics.AsNoTracking().Select(c => c.Id).FirstAsync();
        }

        (await client.GetAsync($"/api/v1/clinic/settings?clinicId={clinicId:D}")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);

        (await client.GetAsync("/api/v1/clinic/settings?platformAdminBypass=true")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        var ok = await client.GetAsync(
            $"/api/v1/clinic/settings?platformAdminBypass=true&clinicId={clinicId:D}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<ClinicSettingsResponse>();
        body!.ClinicId.Should().Be(clinicId);

        (await client.GetAsync(
                $"/api/v1/clinic/settings?platformAdminBypass=true&clinicId={Guid.NewGuid():D}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Empty_Patch_Invalid_Timezone_And_Stale_Version_Are_Rejected()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        var current = await client.GetFromJsonAsync<ClinicSettingsResponse>("/api/v1/clinic/settings");
        current.Should().NotBeNull();

        using var emptyContent = new StringContent(
            """{"expectedVersion":0}""",
            Encoding.UTF8,
            "application/json");
        var empty = await client.PatchAsync("/api/v1/clinic/settings", emptyContent);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var badTz = await client.PatchAsJsonAsync(
            "/api/v1/clinic/settings",
            new UpdateClinicSettingsRequest
            {
                ExpectedVersion = current!.Version,
                DefaultTimeZoneId = "Not/A_Real_Zone",
            },
            PatchJsonOptions);
        badTz.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badTz.Content.ReadAsStringAsync()).Should().Contain(ClinicSettingsErrorCodes.InvalidTimezone);

        var conflict = await client.PatchAsJsonAsync(
            "/api/v1/clinic/settings",
            new UpdateClinicSettingsRequest
            {
                ExpectedVersion = current.Version + 50,
                Name = "Conflict",
            },
            PatchJsonOptions);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain(ClinicSettingsErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Inactive_Membership_Is_Denied()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var staff = await db.StaffMembers
                .Where(s => s.Role == "CLINIC_ADMIN" && s.IsActive)
                .OrderBy(s => s.CreatedAtUtc)
                .FirstAsync();
            staff.IsActive = false;
            await db.SaveChangesAsync();
        }

        // Re-auth so staff context reloads inactive membership.
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");
        (await client.GetAsync("/api/v1/clinic/settings")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
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
        client.DefaultRequestHeaders.Authorization = null;
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
