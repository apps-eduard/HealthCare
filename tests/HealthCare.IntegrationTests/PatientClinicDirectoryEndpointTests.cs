using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class PatientClinicDirectoryEndpointTests : IAsyncLifetime
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
            .WithDatabase("healthcare_patient_clinic_dir")
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
                IntegrationTestHost.ApplyDefaultSettings(builder);
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
    public async Task Anonymous_Clinic_Browse_Returns_401()
    {
        var response = await _client!.GetAsync("/api/v1/patients/me/clinics");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Staff_Cannot_Browse_Patient_Clinic_Directory()
    {
        await AuthenticateAsync(StaffEmail, StaffPassword);
        var response = await _client!.GetAsync("/api/v1/patients/me/clinics");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patient_Cannot_Use_Staff_Clinic_Directory()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/staff-management/clinics");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Linked_Patient_Can_Browse_Active_Clinics()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var page = await client.GetFromJsonAsync<PagedResponse<PatientClinicListItemResponse>>(
            "/api/v1/patients/me/clinics?page=1&pageSize=20");

        page.Should().NotBeNull();
        page!.Items.Should().Contain(c => c.ClinicCode == ClinicCode);
        page.Items.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Name));

        var json = await (await client.GetAsync("/api/v1/patients/me/clinics")).Content.ReadAsStringAsync();
        var lowered = json.ToLowerInvariant();
        lowered.Should().NotContain("organizationid");
        lowered.Should().NotContain("\"clinicid\"");
        lowered.Should().NotContain("createdatutc");
        lowered.Should().NotContain("staffcount");
    }

    [Fact]
    public async Task Search_And_Pagination_Work()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var search = await client.GetFromJsonAsync<PagedResponse<PatientClinicListItemResponse>>(
            "/api/v1/patients/me/clinics?search=Dev%20Clinic&page=1&pageSize=5");
        search!.Items.Should().NotBeEmpty();
        search.Items.Should().Contain(c => c.ClinicCode == ClinicCode);

        var page = await client.GetFromJsonAsync<PagedResponse<PatientClinicListItemResponse>>(
            "/api/v1/patients/me/clinics?page=1&pageSize=1");
        page!.PageSize.Should().Be(1);
        page.Items.Should().HaveCount(1);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Inactive_Clinic_Is_Omitted_And_Detail_Is_Concealed()
    {
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var clinic = await db.Clinics.SingleAsync(c => c.Slug == ClinicCode);
            clinic.IsActive = false;
            await db.SaveChangesAsync();
        }

        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var page = await client.GetFromJsonAsync<PagedResponse<PatientClinicListItemResponse>>(
            "/api/v1/patients/me/clinics");
        page!.Items.Should().NotContain(c => c.ClinicCode == ClinicCode);

        var detail = await client.GetAsync($"/api/v1/patients/me/clinics/{ClinicCode}");
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var clinic = await db.Clinics.SingleAsync(c => c.Slug == ClinicCode);
            clinic.IsActive = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Clinic_Detail_Is_Patient_Safe()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var detail = await client.GetFromJsonAsync<PatientClinicDetailResponse>(
            $"/api/v1/patients/me/clinics/{ClinicCode}");
        detail.Should().NotBeNull();
        detail!.ClinicCode.Should().Be(ClinicCode);
        detail.Name.Should().NotBeNullOrWhiteSpace();

        var json = await (await client.GetAsync($"/api/v1/patients/me/clinics/{ClinicCode}"))
            .Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.TryGetProperty("organizationId", out _).Should().BeFalse();
        root.TryGetProperty("clinicId", out _).Should().BeFalse();
        root.TryGetProperty("createdAtUtc", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Doctors_And_Slots_Remain_Accessible()
    {
        var client = _client!;
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctors = await client.GetFromJsonAsync<List<ClinicDoctorResponse>>(
            $"/api/v1/clinics/{ClinicCode}/doctors");
        doctors.Should().NotBeNull();
        doctors!.Should().NotBeEmpty();

        var doctorId = doctors[0].StaffMemberId;
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1));
        var slotsResponse = await client.GetAsync(
            $"/api/v1/clinics/{ClinicCode}/doctors/{doctorId:D}/available-slots?date={date:yyyy-MM-dd}");
        slotsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Inactive_Patient_Cannot_Browse()
    {
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == PatientEmail);
            var patient = await db.Patients.SingleAsync(p => p.UserId == user.Id);
            patient.IsActive = false;
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/patients/me/clinics");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == PatientEmail);
            var patient = await db.Patients.SingleAsync(p => p.UserId == user.Id);
            patient.IsActive = true;
            await db.SaveChangesAsync();
        }
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
