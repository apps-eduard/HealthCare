using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
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

public sealed class AppointmentReminderEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string StaffAEmail = "doctor.a@healthcare.local";
    private const string StaffAPassword = "ChangeMe_DoctorA_1!";
    private const string StaffBEmail = "doctor.b@healthcare.local";
    private const string StaffBPassword = "ChangeMe_DoctorB_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_reminder_test")
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
    public async Task Appointment_Creation_Creates_Reminder_Records()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(25),
            durationMinutes = 30,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var appt = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var reminders = await db.AppointmentReminders.Where(r => r.AppointmentId == appt!.Id).ToListAsync();
        reminders.Should().Contain(r => r.ReminderType == AppointmentReminderType.Confirmation);
        reminders.Should().Contain(r => r.ReminderType == AppointmentReminderType.Upcoming);
        reminders.Count(r => r.ReminderType == AppointmentReminderType.Confirmation).Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_Cancels_Pending_Reminders()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(26),
            durationMinutes = 30,
        });
        var appt = await create.Content.ReadFromJsonAsync<AppointmentResponse>();
        var cancel = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{appt!.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = appt.Version });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var reminders = await db.AppointmentReminders.Where(r => r.AppointmentId == appt.Id).ToListAsync();
        reminders.Where(r => r.ReminderType != AppointmentReminderType.Cancellation)
            .Should().OnlyContain(r => r.Status == AppointmentReminderStatus.Cancelled);
    }

    [Fact]
    public async Task Staff_Can_List_Reminders_Patient_And_Cross_Clinic_Denied()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(27),
            durationMinutes = 30,
        });
        var appt = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        var patientList = await _client!.GetAsync($"/api/v1/staff/appointments/{appt!.Id}/reminders");
        patientList.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var staffList = await _client!.GetAsync($"/api/v1/staff/appointments/{appt.Id}/reminders");
        staffList.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        var cross = await _client!.GetAsync($"/api/v1/staff/appointments/{appt.Id}/reminders");
        cross.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hangfire_Dashboard_Unavailable_Anonymously()
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/hangfire");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect, HttpStatusCode.OK);
        // Hangfire filter returns empty/forbidden for unauthenticated; ensure not a usable dashboard for anonymous.
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain("Recurring Jobs");
        }
    }

    [Fact]
    public async Task Retry_Endpoint_Rejects_Sent_Reminder()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(28),
            durationMinutes = 30,
        });
        var appt = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var reminder = await db.AppointmentReminders.FirstAsync(
                r => r.AppointmentId == appt!.Id && r.ReminderType == AppointmentReminderType.Confirmation);
            reminder.Status = AppointmentReminderStatus.Sent;
            reminder.SentAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            await AuthenticateAsync(StaffAEmail, StaffAPassword);
            var retry = await _client!.PostAsJsonAsync(
                $"/api/v1/staff/appointments/{appt!.Id}/reminders/retry",
                new RetryAppointmentReminderRequest { ReminderId = reminder.Id });
            retry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    private static DateTimeOffset AlignedFutureSlotUtc(int daysAhead)
    {
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(daysAhead);
        return new DateTimeOffset(localDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified), TimeSpan.FromHours(3))
            .ToUniversalTime();
    }

    private async Task<Guid> GetClinicADoctorStaffIdAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        return await db.StaffMembers
            .Where(s => s.Role == AppRoles.Doctor)
            .Join(db.Clinics.Where(c => c.Slug == "dev-clinic-a"), s => s.ClinicId, c => c.Id, (s, _) => s.Id)
            .SingleAsync();
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
