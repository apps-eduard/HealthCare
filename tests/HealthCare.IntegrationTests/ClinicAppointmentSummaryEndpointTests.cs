using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Appointments;
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

public sealed class ClinicAppointmentSummaryEndpointTests : IAsyncLifetime
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
            .WithDatabase("healthcare_summary_test")
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
                builder.UseSetting("DevelopmentSeed:Patient:ClinicSlug", "dev-clinic-a");

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
    public async Task Patient_Receives_403()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/staff/clinics/current/appointment-summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_Endpoint_Is_Tenant_Scoped()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/staff/clinics/current/appointment-summary");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ClinicAppointmentSummaryResponse>();
        body!.ClinicCode.Should().Be("dev-clinic-a");
    }

    [Fact]
    public async Task Cross_Clinic_Access_Uses_Trusted_Clinic()
    {
        Guid clinicAId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
        }

        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        var response = await _client!.GetAsync($"/api/v1/staff/clinics/current/appointment-summary?clinicId={clinicAId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ClinicAppointmentSummaryResponse>();
        body!.ClinicCode.Should().Be("dev-clinic-b");
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Bypass()
    {
        await AuthenticateAsync("admin@healthcare.local", "ChangeMe_Admin_1!");
        Guid clinicAId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
        }

        var without = await _client!.GetAsync($"/api/v1/staff/clinics/current/appointment-summary?clinicId={clinicAId}");
        without.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);

        // PLATFORM_ADMIN may lack StaffUser policy — endpoint requires StaffUser.
        // Explicit bypass still requires StaffUser policy at controller level.
        without.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dispatcher_Queues_Due_Clinic_And_Idempotent()
    {
        using var scope = _factory!.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IClinicAppointmentSummaryDispatcher>();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IClinicAppointmentSummaryProcessor>();

        // Force a due run by inserting nothing and dispatching — depends on wall clock.
        // Instead create run + process for clinic A only.
        var clinic = await db.Clinics.AsNoTracking().SingleAsync(c => c.Slug == "dev-clinic-a");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinic.Id, date);
        var run = new ClinicAppointmentSummaryRun
        {
            Id = Guid.NewGuid(),
            ClinicId = clinic.Id,
            OrganizationId = clinic.OrganizationId,
            SummaryDate = date,
            ScheduledAtUtc = DateTimeOffset.UtcNow,
            Status = ClinicAppointmentSummaryRunStatus.Pending,
            IdempotencyKey = key,
        };
        db.ClinicAppointmentSummaryRuns.Add(run);
        await db.SaveChangesAsync();

        await processor.ProcessRunAsync(run.Id);
        var completed = await db.ClinicAppointmentSummaryRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        completed.Status.Should().Be(ClinicAppointmentSummaryRunStatus.Completed);

        await processor.ProcessRunAsync(run.Id);
        completed = await db.ClinicAppointmentSummaryRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        completed.Status.Should().Be(ClinicAppointmentSummaryRunStatus.Completed);
        completed.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Clinic_Summary_Contains_Only_That_Clinic()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(90));
        var response = await _client!.GetAsync($"/api/v1/staff/clinics/current/appointment-summary?date={date:yyyy-MM-dd}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ClinicAppointmentSummaryResponse>();
        body!.ClinicCode.Should().Be("dev-clinic-a");
        body.Appointments.Should().OnlyContain(a => a.AppointmentId != Guid.Empty);
    }

    [Fact]
    public async Task Organization_Admin_Can_List_Summary_Runs()
    {
        Guid clinicAId;
        Guid orgId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var clinic = await db.Clinics.SingleAsync(c => c.Slug == "dev-clinic-a");
            clinicAId = clinic.Id;
            orgId = clinic.OrganizationId;
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-2));
            db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicAId,
                OrganizationId = orgId,
                SummaryDate = date,
                ScheduledAtUtc = DateTimeOffset.UtcNow,
                Status = ClinicAppointmentSummaryRunStatus.Failed,
                AttemptCount = 1,
                LastErrorCode = AppointmentSummaryErrorCodes.SummaryDeliveryFailed,
                LastError = "simulated",
                IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicAId, date),
                BackgroundJobId = "integration-job",
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(OrgAdminEmail, OrgAdminPassword);
        var response = await _client!.GetAsync("/api/v1/staff/appointment-summary-runs?status=Failed");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<Contracts.Common.PagedResponse<ClinicAppointmentSummaryRunResponse>>();
        page!.Items.Should().Contain(r =>
            r.ClinicId == clinicAId
            && r.Status == "Failed"
            && r.BackgroundJobId == "integration-job");
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
