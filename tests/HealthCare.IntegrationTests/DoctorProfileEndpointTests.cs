using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HealthCare.Contracts.Doctors;
using HealthCare.Contracts.Identity;
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

public sealed class DoctorProfileEndpointTests : IAsyncLifetime
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
            .WithDatabase("healthcare_doctor_profile_test")
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
        (await client.GetAsync("/api/v1/doctor/profile")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Clinic_Admin_And_Org_Admin_Are_Denied()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/doctor/profile")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = null;
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");
        (await client.GetAsync("/api/v1/doctor/profile")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Authorization = null;
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");
        (await client.GetAsync("/api/v1/doctor/profile")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Doctor_Can_Get_And_Patch_Own_Profile()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "doctor.a@healthcare.local", "ChangeMe_DoctorA_1!");

        var get = await client.GetAsync("/api/v1/doctor/profile");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await get.Content.ReadFromJsonAsync<DoctorProfileResponse>();
        before.Should().NotBeNull();
        before!.StaffMemberId.Should().NotBe(Guid.Empty);
        before.ClinicId.Should().NotBe(Guid.Empty);
        before.OrganizationId.Should().NotBe(Guid.Empty);
        before.Email.Should().Be("doctor.a@healthcare.local");
        before.Role.Should().Be(AppRoles.Doctor);
        before.IsActive.Should().BeTrue();

        var uniqueDisplay = $"DR2 Doctor {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var patch = await client.PatchAsync(
            "/api/v1/doctor/profile",
            new StringContent(
                JsonSerializer.Serialize(
                    new UpdateDoctorProfileRequest
                    {
                        ExpectedVersion = before.Version,
                        DisplayName = uniqueDisplay,
                    },
                    PatchJsonOptions),
                Encoding.UTF8,
                "application/json"));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await patch.Content.ReadFromJsonAsync<DoctorProfileResponse>();
        updated!.DisplayName.Should().Be(uniqueDisplay);
        updated.Email.Should().Be(before.Email);
        updated.Role.Should().Be(before.Role);
        updated.ClinicId.Should().Be(before.ClinicId);
        updated.IsActive.Should().Be(before.IsActive);
        updated.Specialty.Should().Be(before.Specialty);
        updated.FirstName.Should().Be(before.FirstName);
        updated.Version.Should().Be(before.Version + 1);

        var reload = await client.GetAsync("/api/v1/doctor/profile");
        var reloaded = await reload.Content.ReadFromJsonAsync<DoctorProfileResponse>();
        reloaded!.DisplayName.Should().Be(uniqueDisplay);

        var emptyPatch = await client.PatchAsync(
            "/api/v1/doctor/profile",
            new StringContent(
                JsonSerializer.Serialize(new UpdateDoctorProfileRequest { ExpectedVersion = reloaded.Version }, PatchJsonOptions),
                Encoding.UTF8,
                "application/json"));
        emptyPatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stale = await client.PatchAsync(
            "/api/v1/doctor/profile",
            new StringContent(
                JsonSerializer.Serialize(
                    new UpdateDoctorProfileRequest
                    {
                        ExpectedVersion = reloaded.Version - 1,
                        DisplayName = "Stale",
                    },
                    PatchJsonOptions),
                Encoding.UTF8,
                "application/json"));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var longPhone = await client.PatchAsync(
            "/api/v1/doctor/profile",
            new StringContent(
                JsonSerializer.Serialize(
                    new UpdateDoctorProfileRequest
                    {
                        ExpectedVersion = reloaded.Version,
                        ContactPhone = new string('9', 31),
                    },
                    PatchJsonOptions),
                Encoding.UTF8,
                "application/json"));
        longPhone.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await longPhone.Content.ReadAsStringAsync()).Should().Contain(DoctorProfileErrorCodes.InvalidField);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var audit = await db.OrganizationAuditEvents
                .Where(e => e.Action == "doctor_profile_update" && e.ResourceId == updated.StaffMemberId)
                .OrderByDescending(e => e.OccurredAtUtc)
                .FirstOrDefaultAsync();
            audit.Should().NotBeNull();
            audit!.ResultCode.Should().Be("succeeded");
            var auditJson = JsonSerializer.Serialize(audit);
            auditJson.Should().NotContain("+966");
            auditJson.Should().NotContain("ChangeMe_");
            auditJson.Should().NotContain("doctor.a@healthcare.local");
        }

        var overrideDoctor = await client.GetAsync($"/api/v1/doctor/profile?doctorStaffMemberId={Guid.NewGuid():D}");
        overrideDoctor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_Clinic_And_Doctor()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        (await client.GetAsync("/api/v1/doctor/profile")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await client.GetAsync("/api/v1/doctor/profile?platformAdminBypass=true")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        Guid clinicId;
        Guid doctorId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var doctor = await db.StaffMembers.AsNoTracking()
                .Where(s => s.Role == AppRoles.Doctor && s.IsActive)
                .Select(s => new { s.Id, s.ClinicId })
                .FirstAsync();
            clinicId = doctor.ClinicId;
            doctorId = doctor.Id;
        }

        (await client.GetAsync(
                $"/api/v1/doctor/profile?platformAdminBypass=true&clinicId={clinicId:D}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.GetAsync(
                $"/api/v1/doctor/profile?platformAdminBypass=true&doctorStaffMemberId={doctorId:D}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await client.GetAsync(
            $"/api/v1/doctor/profile?platformAdminBypass=true&clinicId={clinicId:D}&doctorStaffMemberId={doctorId:D}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<DoctorProfileResponse>();
        body!.StaffMemberId.Should().Be(doctorId);
        body.ClinicId.Should().Be(clinicId);

        (await client.GetAsync(
                $"/api/v1/doctor/profile?platformAdminBypass=true&clinicId={Guid.NewGuid():D}&doctorStaffMemberId={doctorId:D}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.GetAsync(
                $"/api/v1/doctor/profile?platformAdminBypass=true&clinicId={clinicId:D}&doctorStaffMemberId={Guid.NewGuid():D}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
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
                builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", "doctor.a@healthcare.local");
                builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", "ChangeMe_DoctorA_1!");
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
