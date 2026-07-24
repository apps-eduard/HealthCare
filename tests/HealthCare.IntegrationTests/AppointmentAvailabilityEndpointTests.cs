using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
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

public sealed class AppointmentAvailabilityEndpointTests : IAsyncLifetime
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
            .WithDatabase("healthcare_availability_test")
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
    public async Task Anonymous_Availability_Management_Returns_401()
    {
        var response = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/doctors/{Guid.NewGuid()}/availability",
            new CreateDoctorAvailabilityRequest
            {
                DayOfWeek = "Monday",
                StartLocalTime = "09:00",
                EndLocalTime = "10:00",
                SlotDurationMinutes = 30,
                EffectiveFrom = new DateOnly(2026, 1, 1),
            });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Availability_Management_Returns_403()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var response = await _client!.GetAsync($"/api/v1/staff/doctors/{doctorId}/availability");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Clinic_Admin_Creates_Availability_For_Clinic_Doctor()
    {
        // Seeded doctor may manage own availability (documented rule).
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        await ClearDayAsync(doctorId, DayOfWeek.Sunday);

        var response = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/doctors/{doctorId}/availability",
            new CreateDoctorAvailabilityRequest
            {
                DayOfWeek = "Sunday",
                StartLocalTime = "10:00",
                EndLocalTime = "12:00",
                SlotDurationMinutes = 30,
                EffectiveFrom = new DateOnly(2026, 1, 1),
            });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cross_Clinic_Management_Denied()
    {
        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        var doctorA = await GetClinicADoctorStaffIdAsync();
        var response = await _client!.GetAsync($"/api/v1/staff/doctors/{doctorA}/availability");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Doctor_Listing_Returns_Only_Active_Clinic_Doctors()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync("/api/v1/clinics/dev-clinic-a/doctors");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doctors = await response.Content.ReadFromJsonAsync<List<ClinicDoctorResponse>>();
        var doctorA = await GetClinicADoctorStaffIdAsync();
        var doctorB = await GetClinicBDoctorStaffIdAsync();
        doctors!.Should().Contain(d => d.StaffMemberId == doctorA);
        doctors.Should().NotContain(d => d.StaffMemberId == doctorB);
    }

    [Fact]
    public async Task Available_Slots_Endpoint_Returns_Expected_Slots()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(5);
        var response = await _client!.GetAsync(
            $"/api/v1/clinics/dev-clinic-a/doctors/{doctorId}/available-slots?date={date:yyyy-MM-dd}&durationMinutes=30");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var slots = await response.Content.ReadFromJsonAsync<List<AvailableSlotResponse>>();
        slots.Should().NotBeEmpty();
        slots!.Should().OnlyContain(s => s.DurationMinutes == 30 && s.TimeZoneId == "Asia/Riyadh");
        slots.Should().BeInAscendingOrder(s => s.StartUtc);
    }

    [Fact]
    public async Task Exception_Removes_Affected_Slots()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(6);
        var createEx = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/doctors/{doctorId}/availability-exceptions",
            new CreateDoctorAvailabilityExceptionRequest
            {
                Date = date,
                ExceptionType = "UnavailableFullDay",
                Reason = "Training",
            });
        createEx.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthenticateAsync(PatientEmail, PatientPassword);
        var slots = await _client!.GetAsync(
            $"/api/v1/clinics/dev-clinic-a/doctors/{doctorId}/available-slots?date={date:yyyy-MM-dd}");
        var body = await slots.Content.ReadFromJsonAsync<List<AvailableSlotResponse>>();
        body.Should().BeEmpty();
    }

    [Fact]
    public async Task List_Exceptions_Returns_Created_Exception()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(8);
        var createEx = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/doctors/{doctorId}/availability-exceptions",
            new CreateDoctorAvailabilityExceptionRequest
            {
                Date = date,
                ExceptionType = "UnavailableRange",
                StartLocalTime = "10:00",
                EndLocalTime = "11:00",
                Reason = "Meeting",
            });
        createEx.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await _client!.GetAsync($"/api/v1/staff/doctors/{doctorId}/availability-exceptions");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadFromJsonAsync<List<DoctorAvailabilityExceptionResponse>>();
        body.Should().Contain(e =>
            e.Date == date
            && e.ExceptionType == "UnavailableRange"
            && e.StartLocalTime == "10:00"
            && e.EndLocalTime == "11:00");
    }

    [Fact]
    public async Task Existing_Appointment_Removes_Occupied_Slot_And_Cancel_Frees()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var start = AlignedFutureSlotUtc(12);
        var date = DateOnly.FromDateTime(start.UtcDateTime);

        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = start,
            durationMinutes = 30,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        var slotsAfter = await _client!.GetAsync(
            $"/api/v1/clinics/dev-clinic-a/doctors/{doctorId}/available-slots?date={date:yyyy-MM-dd}&durationMinutes=30");
        var occupied = await slotsAfter.Content.ReadFromJsonAsync<List<AvailableSlotResponse>>();
        occupied!.Should().NotContain(s => s.StartUtc == start);

        var cancel = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{created!.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);

        var slotsFreed = await _client!.GetAsync(
            $"/api/v1/clinics/dev-clinic-a/doctors/{doctorId}/available-slots?date={date:yyyy-MM-dd}&durationMinutes=30");
        var freed = await slotsFreed.Content.ReadFromJsonAsync<List<AvailableSlotResponse>>();
        freed!.Should().Contain(s => s.StartUtc == start);
    }

    [Fact]
    public async Task Booking_Outside_Availability_Returns_409()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        // 03:00 Asia/Riyadh is outside 08:00-20:00
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(8);
        var outside = new DateTimeOffset(localDate.ToDateTime(new TimeOnly(3, 0)), TimeSpan.FromHours(3)).ToUniversalTime();

        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = outside,
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Booking_During_Exception_Returns_409()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var start = AlignedFutureSlotUtc(9);
        var date = DateOnly.FromDateTime(start.UtcDateTime);
        await _client!.PostAsJsonAsync(
            $"/api/v1/staff/doctors/{doctorId}/availability-exceptions",
            new CreateDoctorAvailabilityExceptionRequest
            {
                Date = date,
                ExceptionType = "UnavailableFullDay",
            });

        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = start,
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Staff_Can_List_Doctors_By_Clinic_Id()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();

        var response = await _client!.GetAsync($"/api/v1/staff/clinics/{clinicAId}/doctors");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doctors = await response.Content.ReadFromJsonAsync<List<ClinicDoctorResponse>>();
        doctors.Should().NotBeNull();
        doctors!.Should().OnlyContain(d => d.ClinicId == clinicAId);
        doctors.Should().Contain(d => !string.IsNullOrWhiteSpace(d.ClinicTimeZoneId));
    }

    [Fact]
    public async Task Booking_With_Invalid_Slot_Boundary_Returns_409()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(10);
        // 09:15 is not on a 30-minute boundary from 08:00
        var offBoundary = new DateTimeOffset(localDate.ToDateTime(new TimeOnly(9, 15)), TimeSpan.FromHours(3))
            .ToUniversalTime();

        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = offBoundary,
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Valid_Patient_And_Staff_Booking_Succeed()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var patientStart = AlignedFutureSlotUtc(14);
        var patientCreate = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = patientStart,
            durationMinutes = 30,
        });
        patientCreate.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var patientId = await GetSeedPatientIdAsync();
        var staffStart = AlignedFutureSlotUtc(14).AddMinutes(30);
        var staffCreate = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = staffStart,
            durationMinutes = 30,
        });
        staffCreate.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Clinic_Timezone_Conversion_Produces_Correct_Utc()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(16);
        var localNine = new DateTimeOffset(localDate.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(3));
        var expectedUtc = localNine.ToUniversalTime();

        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = expectedUtc,
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AppointmentResponse>();
        body!.AppointmentDateUtc.UtcDateTime.Should().Be(expectedUtc.UtcDateTime);
    }

    [Fact]
    public async Task Stale_Availability_Update_Returns_409()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var list = await _client!.GetAsync($"/api/v1/staff/doctors/{doctorId}/availability");
        var rows = await list.Content.ReadFromJsonAsync<List<DoctorAvailabilityResponse>>();
        var row = rows!.First();

        var stale = await _client!.PatchAsJsonAsync(
            $"/api/v1/staff/doctors/{doctorId}/availability/{row.Id}",
            new UpdateDoctorAvailabilityRequest
            {
                ExpectedVersion = row.Version + 99,
                IsActive = false,
            });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Client_Tenant_Manipulation_Fails()
    {
        typeof(CreateDoctorAvailabilityRequest).GetProperty("OrganizationId").Should().BeNull();
        typeof(CreateDoctorAvailabilityRequest).GetProperty("ClinicId").Should().BeNull();
        await Task.CompletedTask;
    }

    private static DateTimeOffset AlignedFutureSlotUtc(int daysAhead)
    {
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(daysAhead);
        return new DateTimeOffset(localDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified), TimeSpan.FromHours(3))
            .ToUniversalTime();
    }

    private async Task ClearDayAsync(Guid doctorId, DayOfWeek day)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var rows = await db.DoctorAvailabilities
            .Where(a => a.DoctorStaffMemberId == doctorId && a.DayOfWeek == day)
            .ToListAsync();
        db.DoctorAvailabilities.RemoveRange(rows);
        await db.SaveChangesAsync();
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
