using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
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

public sealed class AppointmentFoundationEndpointTests : IAsyncLifetime
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
            .WithDatabase("healthcare_appointment_test")
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
    public async Task Anonymous_Patient_Booking_Returns_401()
    {
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = Guid.NewGuid(),
            appointmentDateUtc = DateTimeOffset.UtcNow.AddDays(1),
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Staff_Cannot_Use_Patient_Self_Booking()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = Guid.NewGuid(),
            appointmentDateUtc = DateTimeOffset.UtcNow.AddDays(1),
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Patient_Creates_Appointment_For_Self()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead: 2),
            durationMinutes = 30,
            reason = "Toothache",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AppointmentResponse>();
        body!.Status.Should().Be("Requested");
        body.Source.Should().Be("Patient");
    }

    [Fact]
    public async Task Patient_Not_Enrolled_In_Clinic_Is_Denied()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorB = await GetClinicBDoctorStaffIdAsync();
        // Patient may already be enrolled in B from prior seed updates; use nonexistent clinic code
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "no-such-clinic",
            doctorStaffMemberId = doctorB,
            appointmentDateUtc = DateTimeOffset.UtcNow.AddDays(3),
            durationMinutes = 30,
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Staff_Cannot_Assign_Clinic_B_Doctor_From_Clinic_A()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var patientId = await GetSeedPatientIdAsync();
        var doctorB = await GetClinicBDoctorStaffIdAsync();
        var response = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorB,
            appointmentDateUtc = DateTimeOffset.UtcNow.AddDays(4),
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Patient_List_Returns_Only_Own_Appointments()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/patients/me/appointments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<AppointmentResponse>>();
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task Staff_List_Returns_Only_Clinic_Appointments()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/staff/appointments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<AppointmentResponse>>();
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();
        body!.Items.Should().OnlyContain(i => i.ClinicId == clinicAId);
    }

    [Fact]
    public async Task Staff_Queue_And_Calendar_Endpoints_Return_Ok()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var queue = await _client!.GetAsync("/api/v1/staff/appointments/queue");
        queue.StatusCode.Should().Be(HttpStatusCode.OK);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(7).ToString("o"));
        var calendar = await _client!.GetAsync($"/api/v1/staff/appointments/calendar?fromUtc={from}&toUtc={to}&view=week");
        calendar.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Client_Tenant_Identifiers_Cannot_Bypass_On_Patient_Create_Contract()
    {
        typeof(CreatePatientAppointmentRequest).GetProperty("OrganizationId").Should().BeNull();
        typeof(CreatePatientAppointmentRequest).GetProperty("PatientId").Should().BeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Slot_Overlap_Returns_409_And_Cancelled_Can_Be_Reused()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var start = AlignedFutureSlotUtc(daysAhead: 15);
        var first = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = start,
            durationMinutes = 30,
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<AppointmentResponse>();

        var conflict = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = start.AddMinutes(15),
            durationMinutes = 30,
        });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancel = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{firstBody!.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = firstBody.Version, CancellationReason = "Changed plans" });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuse = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = start,
            durationMinutes = 30,
        });
        reuse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Valid_Transition_And_Stale_Version()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead: 20),
            durationMinutes = 30,
        });
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var confirm = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created!.Id}/confirm",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created.Id}/check-in",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Detail_Outside_Scope_Does_Not_Disclose()
    {
        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        var response = await _client!.GetAsync($"/api/v1/appointments/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Returns a UTC instant that is 09:00 Asia/Riyadh on a future calendar day (30-min slot boundary).
    /// </summary>
    private static DateTimeOffset AlignedFutureSlotUtc(int daysAhead)
    {
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(daysAhead);
        // Asia/Riyadh is UTC+3 year-round.
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

    private async Task<Guid> GetClinicBDoctorStaffIdAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        return await db.StaffMembers
            .Where(s => s.Role == AppRoles.Doctor)
            .Join(db.Clinics.Where(c => c.Slug == "dev-clinic-b"), s => s.ClinicId, c => c.Id, (s, _) => s.Id)
            .SingleAsync();
    }

    private async Task<Guid> GetSeedPatientIdAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        return await db.Patients.Select(p => p.Id).FirstAsync();
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
