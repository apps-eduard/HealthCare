using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class ClinicReportsEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_clinic_reports_test")
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
        (await client.GetAsync("/api/v1/clinic/reports/appointments")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/clinic/reports/appointments")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Organization_Admin_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");
        (await client.GetAsync("/api/v1/clinic/reports/appointments")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clinic_Admin_Reports_Are_Clinic_Scoped_And_Aggregate_Only()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        Guid clinicId;
        Guid otherClinicId;
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

            var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
            var clinicTz = await db.Clinics.AsNoTracking()
                .Where(c => c.Id == clinicId)
                .Select(c => c.TimeZoneId)
                .SingleAsync();
            var today = converter.GetClinicDate(DateTimeOffset.UtcNow, clinicTz);
            from = today.AddDays(-7).ToString("yyyy-MM-dd");
            to = today.ToString("yyyy-MM-dd");

            await SeedScopedReportDataAsync(db, clinicId, otherClinicId, today, converter, clinicTz);
        }

        var qs = $"fromDate={from}&toDate={to}";
        var appointmentsResponse = await client.GetAsync($"/api/v1/clinic/reports/appointments?{qs}");
        appointmentsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var appointmentsJson = await appointmentsResponse.Content.ReadAsStringAsync();
        var appointments = JsonSerializer.Deserialize<ClinicAppointmentReportResponse>(
            appointmentsJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        appointments.Should().NotBeNull();
        appointments!.Context.ClinicId.Should().Be(clinicId);
        appointments.TotalAppointments.Should().BeGreaterThanOrEqualTo(2);
        appointments.ByStatus.Should().NotBeEmpty();
        appointments.VolumeByDate.Should().NotBeEmpty();
        appointments.CancellationNoShow.NoShowCount.Should().BeGreaterThanOrEqualTo(1);

        var doctors = await client.GetFromJsonAsync<ClinicDoctorAppointmentsReportResponse>(
            $"/api/v1/clinic/reports/doctors?{qs}");
        doctors!.Doctors.Should().NotBeEmpty();
        doctors.Doctors.Should().OnlyContain(d => d.DoctorDisplayName.Length > 0);

        var patients = await client.GetFromJsonAsync<ClinicPatientEnrollmentReportResponse>(
            $"/api/v1/clinic/reports/patients?{qs}");
        patients!.TotalClinicPatients.Should().BeGreaterThanOrEqualTo(1);
        patients.ActiveEnrollmentCount.Should().BeGreaterThanOrEqualTo(1);

        var ops = await client.GetFromJsonAsync<ClinicOperationsReportResponse>(
            $"/api/v1/clinic/reports/reminders?{qs}");
        ops!.FailedReminderCount.Should().BeGreaterThanOrEqualTo(1);
        ops.FailedSummaryRunCount.Should().BeGreaterThanOrEqualTo(1);

        foreach (var json in new[]
                 {
                     appointmentsJson,
                     JsonSerializer.Serialize(doctors),
                     JsonSerializer.Serialize(patients),
                     JsonSerializer.Serialize(ops),
                 })
        {
            json.Should().NotContain("PatientName");
            json.Should().NotContain("diagnosis");
            json.Should().NotContain("MaxClinics");
            json.ToLowerInvariant().Should().NotContain("billing");
            json.Should().NotContain("MedicalNote");
        }

        var overrideResponse = await client.GetAsync(
            $"/api/v1/clinic/reports/appointments?clinicId={otherClinicId:D}&{qs}");
        overrideResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.GetAsync($"/api/v1/clinic/reports/appointments?fromDate={from}&toDate=2099-01-01"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/clinic/reports/appointments?fromDate=2026-01-01&toDate=2026-04-05"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/v1/clinic/reports/appointments/export")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_ClinicId()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        (await client.GetAsync("/api/v1/clinic/reports/appointments")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        (await client.GetAsync("/api/v1/clinic/reports/appointments?platformAdminBypass=true")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        Guid clinicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            clinicId = await db.Clinics.AsNoTracking().Select(c => c.Id).FirstAsync();
        }

        var ok = await client.GetAsync(
            $"/api/v1/clinic/reports/appointments?platformAdminBypass=true&clinicId={clinicId:D}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ok.Content.ReadFromJsonAsync<ClinicAppointmentReportResponse>();
        body!.Context.ClinicId.Should().Be(clinicId);

        var missing = await client.GetAsync(
            $"/api/v1/clinic/reports/appointments?platformAdminBypass=true&clinicId={Guid.NewGuid():D}");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task SeedScopedReportDataAsync(
        HealthCareDbContext db,
        Guid clinicId,
        Guid otherClinicId,
        DateOnly today,
        ClinicTimeZoneConverter converter,
        string clinicTz)
    {
        var orgId = await db.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => c.OrganizationId)
            .SingleAsync();

        var doctor = await db.StaffMembers.FirstAsync(s => s.ClinicId == clinicId && s.Role == AppRoles.Doctor);
        var otherDoctor = await db.StaffMembers
            .FirstOrDefaultAsync(s => s.ClinicId == otherClinicId && s.Role == AppRoles.Doctor);
        if (otherDoctor is null)
        {
            return;
        }

        async Task<Appointment> AddAppointment(
            Guid targetClinicId,
            Guid doctorStaffMemberId,
            Guid createdByUserId,
            AppointmentStatus status)
        {
            var patient = new Patient
            {
                Id = Guid.NewGuid(),
                UserId = null,
                FirstName = "Report",
                LastName = "Seed",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            var enrollment = new ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = targetClinicId,
                PatientId = patient.Id,
                LocalPatientNumber = "R-" + Guid.NewGuid().ToString("N")[..8],
                Status = ClinicPatientStatus.Active,
                Version = 0,
                RegisteredAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                ClinicId = targetClinicId,
                PatientId = patient.Id,
                ClinicPatientId = enrollment.Id,
                DoctorStaffMemberId = doctorStaffMemberId,
                AppointmentDateUtc = converter.ToUtc(today, new TimeOnly(11, 0), clinicTz),
                DurationMinutes = 30,
                Status = status,
                Source = AppointmentSource.Staff,
                CreatedByUserId = createdByUserId,
                Version = 0,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Patients.Add(patient);
            db.ClinicPatients.Add(enrollment);
            db.Appointments.Add(appointment);
            return appointment;
        }

        var ownCompleted = await AddAppointment(
            clinicId, doctor.Id, doctor.UserId, AppointmentStatus.Completed);
        _ = await AddAppointment(clinicId, doctor.Id, doctor.UserId, AppointmentStatus.NoShow);
        var otherAppt = await AddAppointment(
            otherClinicId, otherDoctor.Id, otherDoctor.UserId, AppointmentStatus.CancelledByPatient);

        db.AppointmentReminders.Add(new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            AppointmentId = ownCompleted.Id,
            ReminderType = AppointmentReminderType.Upcoming,
            ScheduledAtUtc = converter.ToUtc(today, new TimeOnly(8, 0), clinicTz),
            Status = AppointmentReminderStatus.Failed,
            AttemptCount = 1,
            IdempotencyKey = AppointmentReminder.BuildIdempotencyKey(ownCompleted.Id, AppointmentReminderType.Upcoming)
                + ":ca8",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.AppointmentReminders.Add(new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            AppointmentId = otherAppt.Id,
            ReminderType = AppointmentReminderType.Upcoming,
            ScheduledAtUtc = converter.ToUtc(today, new TimeOnly(8, 0), clinicTz),
            Status = AppointmentReminderStatus.Failed,
            AttemptCount = 1,
            IdempotencyKey = "other-clinic-failed-reminder-" + Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            OrganizationId = orgId,
            SummaryDate = today,
            ScheduledAtUtc = DateTimeOffset.UtcNow,
            Status = ClinicAppointmentSummaryRunStatus.Failed,
            AttemptCount = 1,
            IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicId, today) + ":ca8",
            AppointmentCount = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
        {
            Id = Guid.NewGuid(),
            ClinicId = otherClinicId,
            OrganizationId = orgId,
            SummaryDate = today,
            ScheduledAtUtc = DateTimeOffset.UtcNow,
            Status = ClinicAppointmentSummaryRunStatus.Failed,
            AttemptCount = 1,
            IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(otherClinicId, today) + ":ca8",
            AppointmentCount = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
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
