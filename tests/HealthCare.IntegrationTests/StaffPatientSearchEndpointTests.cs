using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
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

public sealed class StaffPatientSearchEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string StaffAEmail = "doctor.a@healthcare.local";
    private const string StaffAPassword = "ChangeMe_DoctorA_1!";
    private const string StaffBEmail = "doctor.b@healthcare.local";
    private const string StaffBPassword = "ChangeMe_DoctorB_1!";
    private const string OrgAdminEmail = "orgadmin@healthcare.local";
    private const string OrgAdminPassword = "ChangeMe_OrgAdmin_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_staff_patient_test")
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
                builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", StaffAEmail);
                builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", StaffAPassword);
                builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffEmail", StaffBEmail);
                builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffPassword", StaffBPassword);
                builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminEmail", OrgAdminEmail);
                builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminPassword", OrgAdminPassword);

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
    public async Task Anonymous_Search_Returns_401()
    {
        var response = await _client!.GetAsync("/api/v1/staff/patients");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Search_Returns_403()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/staff/patients");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clinic_A_Staff_Sees_Clinic_A_Patients_Only()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/staff/patients");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>();
        body.Should().NotBeNull();
        body!.Items.Should().NotBeEmpty();
        body.Items.Should().OnlyContain(i => i.LocalPatientNumber == "DEV-P-0001"
                                             || i.ClinicId != Guid.Empty);
        body.Items.Should().OnlyContain(i => i.LocalPatientNumber != "DEV-P-B-0001");
    }

    [Fact]
    public async Task Clinic_A_Staff_Cannot_See_Clinic_B_Local_Number()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/staff/patients");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>();
        body!.Items.Select(i => i.LocalPatientNumber).Should().NotContain("DEV-P-B-0001");
    }

    [Fact]
    public async Task Organization_Admin_Sees_Multiple_Clinics_In_Org()
    {
        await AuthenticateAsync(OrgAdminEmail, OrgAdminPassword);
        var response = await _client!.GetAsync("/api/v1/staff/patients");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>();
        body!.Items.Select(i => i.LocalPatientNumber).Should()
            .Contain(["DEV-P-0001", "DEV-P-B-0001"]);
    }

    [Fact]
    public async Task Client_ClinicId_Cannot_Bypass_Clinic_Scope()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var clinicBId = await db.Clinics.Where(c => c.Slug == "dev-clinic-b").Select(c => c.Id).SingleAsync();

        var response = await _client!.GetAsync($"/api/v1/staff/patients?clinicId={clinicBId}");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>();
        body!.Items.Should().NotContain(i => i.LocalPatientNumber == "DEV-P-B-0001");
        body.Items.Should().Contain(i => i.LocalPatientNumber == "DEV-P-0001");
    }

    [Fact]
    public async Task Client_OrganizationId_Query_Is_Ignored()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync($"/api/v1/staff/patients?organizationId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>();
        body!.Items.Should().Contain(i => i.LocalPatientNumber == "DEV-P-0001");
        body.Items.Should().NotContain(i => i.LocalPatientNumber == "DEV-P-B-0001");
    }

    [Fact]
    public async Task Pagination_Metadata_Is_Correct()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/staff/patients?page=1&pageSize=1");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(1);
        body.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        body.TotalPages.Should().BeGreaterThanOrEqualTo(1);
        body.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Search_Filters_Return_Expected_Results()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/staff/patients?search=Dev&localPatientNumber=DEV-P-0001");
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<StaffPatientSummaryResponse>>();
        body!.Items.Should().ContainSingle(i => i.LocalPatientNumber == "DEV-P-0001");
    }

    [Fact]
    public async Task Patient_Detail_Outside_Scope_Does_Not_Disclose_Existence()
    {
        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var clinicAPatientId = await db.ClinicPatients
            .Where(cp => cp.LocalPatientNumber == "DEV-P-0001")
            .Select(cp => cp.PatientId)
            .SingleAsync();

        // Clinic B staff looking up patient who IS enrolled in Clinic B too in seed —
        // create an A-only patient for this assertion.
        var aOnlyPatientId = Guid.NewGuid();
        var clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
        db.Patients.Add(new Domain.Patients.Patient
        {
            Id = aOnlyPatientId,
            FirstName = "Only",
            LastName = "A",
            IsActive = true,
        });
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicAId,
            PatientId = aOnlyPatientId,
            LocalPatientNumber = "DEV-P-A-ONLY",
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var response = await _client!.GetAsync($"/api/v1/staff/patients/{aOnlyPatientId}");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("Only");
        json.Should().NotContain("DEV-P-A-ONLY");
        _ = clinicAPatientId;
    }

    [Fact]
    public async Task ClinicPatient_Status_Update_Succeeds_Within_Scope()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var patientId = await db.ClinicPatients
            .Where(cp => cp.LocalPatientNumber == "DEV-P-0001")
            .Select(cp => cp.PatientId)
            .SingleAsync();

        var detail = await _client!.GetFromJsonAsync<StaffPatientDetailResponse>($"/api/v1/staff/patients/{patientId}");
        var response = await _client!.PatchAsJsonAsync(
            $"/api/v1/staff/patients/{patientId}/clinic-profile",
            new UpdateClinicPatientRequest { ExpectedVersion = detail!.Version, Status = "Inactive" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<StaffPatientDetailResponse>();
        updated!.ClinicPatientStatus.Should().Be("Inactive");

        // restore
        await _client!.PatchAsJsonAsync(
            $"/api/v1/staff/patients/{patientId}/clinic-profile",
            new UpdateClinicPatientRequest { ExpectedVersion = updated.Version, Status = "Active" });
    }

    [Fact]
    public async Task Cross_Clinic_Update_Is_Denied()
    {
        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var aOnlyPatientId = Guid.NewGuid();
        var clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
        db.Patients.Add(new Domain.Patients.Patient
        {
            Id = aOnlyPatientId,
            FirstName = "Cross",
            LastName = "Deny",
            IsActive = true,
        });
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicAId,
            PatientId = aOnlyPatientId,
            LocalPatientNumber = "DEV-P-CROSS",
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var response = await _client!.PatchAsJsonAsync(
            $"/api/v1/staff/patients/{aOnlyPatientId}/clinic-profile",
            new UpdateClinicPatientRequest { ExpectedVersion = 0, Status = "Inactive" });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stale_Version_Returns_409()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var patientId = await db.ClinicPatients
            .Where(cp => cp.LocalPatientNumber == "DEV-P-0001")
            .Select(cp => cp.PatientId)
            .SingleAsync();

        var response = await _client!.PatchAsJsonAsync(
            $"/api/v1/staff/patients/{patientId}/clinic-profile",
            new UpdateClinicPatientRequest { ExpectedVersion = 9999, Status = "Inactive" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task AuthenticateAsync(string email, string password)
    {
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
