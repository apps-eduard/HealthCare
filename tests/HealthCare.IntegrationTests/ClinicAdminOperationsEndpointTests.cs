using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
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

public sealed class ClinicAdminOperationsEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string StaffAEmail = "doctor.a@healthcare.local";
    private const string StaffAPassword = "ChangeMe_DoctorA_1!";
    private const string StaffBEmail = "doctor.b@healthcare.local";
    private const string StaffBPassword = "ChangeMe_DoctorB_1!";
    private const string ClinicAdminEmail = "clinicadmin@healthcare.local";
    private const string ClinicAdminPassword = "ChangeMe_ClinicAdmin_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_clinic_admin_ops_test")
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
                builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", StaffAEmail);
                builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", StaffAPassword);
                builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffEmail", StaffBEmail);
                builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffPassword", StaffBPassword);
                builder.UseSetting("DevelopmentSeed:Patient:ClinicAdminEmail", ClinicAdminEmail);
                builder.UseSetting("DevelopmentSeed:Patient:ClinicAdminPassword", ClinicAdminPassword);
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
    public async Task Clinic_Admin_Lists_Only_Own_Clinic_Reminders_And_Filters()
    {
        var own = await CreateAppointmentAsync(StaffAEmail, StaffAPassword, daysAhead: 40);
        var sibling = await CreateAppointmentAsync(StaffBEmail, StaffBPassword, daysAhead: 41);

        await MarkReminderFailedAsync(own.Id);
        await MarkReminderFailedAsync(sibling.Id);

        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var response = await _client!.GetAsync(
            "/api/v1/staff/reminders?status=Failed&page=1&pageSize=50");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<AppointmentReminderResponse>>();
        body!.Items.Should().Contain(r => r.AppointmentId == own.Id);
        body.Items.Should().NotContain(r => r.AppointmentId == sibling.Id);
        body.Items.Should().OnlyContain(r => r.ClinicId == own.ClinicId);
        body.Items.Should().OnlyContain(r => r.Status == "Failed");

        var json = await response.Content.ReadAsStringAsync();
        json.ToLowerInvariant().Should().NotContain("password");
        json.ToLowerInvariant().Should().NotContain("api_key");
        json.ToLowerInvariant().Should().NotContain("medical_note");
    }

    [Fact]
    public async Task Clinic_Admin_Can_Retry_Eligible_Own_Clinic_Reminder_Idempotently()
    {
        var own = await CreateAppointmentAsync(StaffAEmail, StaffAPassword, daysAhead: 42);
        var reminderId = await MarkReminderFailedAsync(own.Id);

        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var first = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{own.Id:D}/reminders/retry",
            new RetryAppointmentReminderRequest { ReminderId = reminderId });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var retried = await first.Content.ReadFromJsonAsync<AppointmentReminderResponse>();
        retried!.Status.Should().Be("Pending");
        retried.BackgroundJobId.Should().NotBeNullOrWhiteSpace();

        var second = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{own.Id:D}/reminders/retry",
            new RetryAppointmentReminderRequest { ReminderId = reminderId });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await second.Content.ReadFromJsonAsync<AppointmentReminderResponse>();
        again!.Status.Should().Be("Pending");

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var audits = await db.OrganizationAuditEvents.AsNoTracking()
            .Where(e => e.Action == "reminder_retry" && e.ResourceId == reminderId)
            .ToListAsync();
        audits.Should().NotBeEmpty();
        var auditJson = System.Text.Json.JsonSerializer.Serialize(audits);
        auditJson.ToLowerInvariant().Should().NotContain("password");
        auditJson.ToLowerInvariant().Should().NotContain("secret");
        auditJson.ToLowerInvariant().Should().NotContain("stack");
    }

    [Fact]
    public async Task Clinic_Admin_Cross_Clinic_And_Non_Retryable_Reminder_Denied()
    {
        var own = await CreateAppointmentAsync(StaffAEmail, StaffAPassword, daysAhead: 43);
        var sibling = await CreateAppointmentAsync(StaffBEmail, StaffBPassword, daysAhead: 44);
        var siblingReminderId = await MarkReminderFailedAsync(sibling.Id);

        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var cross = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{sibling.Id:D}/reminders/retry",
            new RetryAppointmentReminderRequest { ReminderId = siblingReminderId });
        cross.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var sent = await db.AppointmentReminders.FirstAsync(r => r.AppointmentId == own.Id);
            sent.Status = AppointmentReminderStatus.Sent;
            await db.SaveChangesAsync();

            var denySent = await _client!.PostAsJsonAsync(
                $"/api/v1/staff/appointments/{own.Id:D}/reminders/retry",
                new RetryAppointmentReminderRequest { ReminderId = sent.Id });
            denySent.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task Clinic_Admin_Lists_Only_Own_Clinic_Summaries_And_Can_Retry()
    {
        Guid clinicAId;
        Guid clinicBId;
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-3));
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
            clinicBId = await db.Clinics.Where(c => c.Slug == "dev-clinic-b").Select(c => c.Id).SingleAsync();
            var orgId = await db.Clinics.Where(c => c.Id == clinicAId).Select(c => c.OrganizationId).SingleAsync();

            db.ClinicAppointmentSummaryRuns.AddRange(
                new ClinicAppointmentSummaryRun
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinicAId,
                    OrganizationId = orgId,
                    SummaryDate = date,
                    ScheduledAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                    Status = ClinicAppointmentSummaryRunStatus.Failed,
                    AttemptCount = 1,
                    LastError = "simulated",
                    LastErrorCode = AppointmentSummaryErrorCodes.SummaryDeliveryFailed,
                    IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicAId, date),
                },
                new ClinicAppointmentSummaryRun
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinicBId,
                    OrganizationId = orgId,
                    SummaryDate = date,
                    ScheduledAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
                    Status = ClinicAppointmentSummaryRunStatus.Failed,
                    AttemptCount = 1,
                    LastError = "simulated",
                    LastErrorCode = AppointmentSummaryErrorCodes.SummaryDeliveryFailed,
                    IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicBId, date),
                });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var list = await _client!.GetAsync(
            $"/api/v1/staff/appointment-summary-runs?status=Failed&fromDate={date:yyyy-MM-dd}&toDate={date:yyyy-MM-dd}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadFromJsonAsync<PagedResponse<ClinicAppointmentSummaryRunResponse>>();
        body!.Items.Should().Contain(r => r.ClinicId == clinicAId);
        body.Items.Should().NotContain(r => r.ClinicId == clinicBId);

        var retry = await _client!.PostAsync(
            $"/api/v1/staff/clinics/{clinicAId:D}/appointment-summary/{date:yyyy-MM-dd}/retry",
            content: null);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var retried = await retry.Content.ReadFromJsonAsync<ClinicAppointmentSummaryRunResponse>();
        retried!.Status.Should().Be("Pending");
        retried.ClinicId.Should().Be(clinicAId);

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var audits = await db.OrganizationAuditEvents.AsNoTracking()
                .Where(e => e.Action == "summary_retry")
                .OrderByDescending(e => e.OccurredAtUtc)
                .Take(3)
                .ToListAsync();
            audits.Should().NotBeEmpty();
            var json = System.Text.Json.JsonSerializer.Serialize(audits);
            json.ToLowerInvariant().Should().NotContain("secret");
            json.ToLowerInvariant().Should().NotContain("stacktrace");
        }
    }

    [Fact]
    public async Task Clinic_Admin_Operations_Health_Is_Clinic_Scoped()
    {
        Guid clinicAId;
        Guid clinicBId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
            clinicBId = await db.Clinics.Where(c => c.Slug == "dev-clinic-b").Select(c => c.Id).SingleAsync();
            var orgId = await db.Clinics.Where(c => c.Id == clinicAId).Select(c => c.OrganizationId).SingleAsync();
            var date = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-5));
            db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicBId,
                OrganizationId = orgId,
                SummaryDate = date,
                ScheduledAtUtc = DateTimeOffset.UtcNow,
                Status = ClinicAppointmentSummaryRunStatus.Failed,
                AttemptCount = 1,
                IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicBId, date),
            });
            await db.SaveChangesAsync();
        }

        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var response = await _client!.GetAsync($"/api/v1/staff/operations/health?clinicId={clinicBId:D}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var health = await response.Content.ReadFromJsonAsync<StaffOperationsHealthResponse>();
        health!.ClinicId.Should().Be(clinicAId);
        health.FailedSummaryRunCount.Should().Be(0);
        health.ReminderSenderMode.Should().NotBeNullOrWhiteSpace();

        var json = await response.Content.ReadAsStringAsync();
        json.ToLowerInvariant().Should().NotContain("connectionstring");
        json.ToLowerInvariant().Should().NotContain("password");
        json.ToLowerInvariant().Should().NotContain("apikey");
    }

    [Fact]
    public async Task Anonymous_Patient_And_Platform_Admin_Bypass_Rules()
    {
        (await _client!.GetAsync("/api/v1/staff/reminders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client!.GetAsync("/api/v1/staff/operations/health")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await AuthenticateAsync(PatientEmail, PatientPassword);
        (await _client!.GetAsync("/api/v1/staff/reminders")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _client!.GetAsync("/api/v1/staff/operations/health")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await AuthenticateAsync("admin@healthcare.local", "ChangeMe_Admin_1!");
        Guid clinicAId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
        }

        var denied = await _client!.GetAsync($"/api/v1/staff/reminders?clinicId={clinicAId:D}");
        denied.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);

        var allowed = await _client!.GetAsync(
            $"/api/v1/staff/reminders?clinicId={clinicAId:D}&platformAdminBypass=true");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        var healthDenied = await _client!.GetAsync($"/api/v1/staff/operations/health?clinicId={clinicAId:D}");
        healthDenied.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);

        var healthOk = await _client!.GetAsync(
            $"/api/v1/staff/operations/health?clinicId={clinicAId:D}&platformAdminBypass=true");
        healthOk.StatusCode.Should().Be(HttpStatusCode.OK);
        var health = await healthOk.Content.ReadFromJsonAsync<StaffOperationsHealthResponse>();
        health!.ClinicId.Should().Be(clinicAId);
    }

    private async Task<AppointmentResponse> CreateAppointmentAsync(string staffEmail, string staffPassword, int daysAhead)
    {
        var clinicCode = staffEmail == StaffBEmail ? "dev-clinic-b" : "dev-clinic-a";
        await AuthenticateAsync(PatientEmail, PatientPassword);
        Guid doctorId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            doctorId = await db.StaffMembers
                .Where(s => s.Role == AppRoles.Doctor)
                .Join(db.Clinics.Where(c => c.Slug == clinicCode), s => s.ClinicId, c => c.Id, (s, _) => s.Id)
                .SingleAsync();
        }

        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead),
            durationMinutes = 30,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await create.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    private async Task<Guid> MarkReminderFailedAsync(Guid appointmentId)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var reminder = await db.AppointmentReminders.FirstAsync(r => r.AppointmentId == appointmentId);
        reminder.Status = AppointmentReminderStatus.Failed;
        reminder.LastError = "simulated_delivery_failure";
        reminder.AttemptCount = 1;
        await db.SaveChangesAsync();
        return reminder.Id;
    }

    private static DateTimeOffset AlignedFutureSlotUtc(int daysAhead)
    {
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(daysAhead);
        return new DateTimeOffset(localDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified), TimeSpan.FromHours(3))
            .ToUniversalTime();
    }

    private async Task AuthenticateAsync(string email, string password)
    {
        var login = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        _client!.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
