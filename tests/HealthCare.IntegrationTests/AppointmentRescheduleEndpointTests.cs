using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class AppointmentRescheduleEndpointTests : IAsyncLifetime
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
            .WithDatabase("healthcare_reschedule_test")
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
    public async Task Anonymous_Reschedule_Returns_401()
    {
        var response = await _client!.PostAsJsonAsync($"/api/v1/appointments/{Guid.NewGuid()}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(5),
            durationMinutes = 30,
            expectedVersion = 0,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Reschedules_Own_Appointment()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId, AlignedFutureSlotUtc(30));

        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(31),
            durationMinutes = 30,
            expectedVersion = created.Version,
            reason = "Moved",
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await reschedule.Content.ReadFromJsonAsync<AppointmentResponse>();
        body!.Id.Should().Be(created.Id);
        body.Version.Should().Be(created.Version + 1);
    }

    [Fact]
    public async Task Patient_Cannot_Reschedule_Another_Appointment()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var patientId = await GetSeedPatientIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(32),
            durationMinutes = 30,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        // No other patient seeded with auth; use staff as non-owner patient path by authenticating patient
        // against an appointment that belongs to the seeded patient — create a second patient appointment
        // via staff for a fabricated patient is harder. Instead verify not-found for random id as patient.
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var foreign = await _client!.PostAsJsonAsync($"/api/v1/appointments/{Guid.NewGuid()}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(33),
            durationMinutes = 30,
            expectedVersion = 0,
        });
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Seeded patient owns `created`; create another appointment for a different patient via DB then deny.
        Guid otherAppointmentId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var otherPatient = new Domain.Patients.Patient
            {
                Id = Guid.NewGuid(),
                FirstName = "Other",
                LastName = "P",
                IsActive = true,
            };
            db.Patients.Add(otherPatient);
            var clinicA = await db.Clinics.SingleAsync(c => c.Slug == "dev-clinic-a");
            var enrollment = new Domain.Patients.ClinicPatient
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicA.Id,
                PatientId = otherPatient.Id,
                LocalPatientNumber = "A-OTHER",
                Status = Domain.Patients.ClinicPatientStatus.Active,
            };
            db.ClinicPatients.Add(enrollment);
            var appt = new Appointment
            {
                Id = Guid.NewGuid(),
                OrganizationId = clinicA.OrganizationId,
                ClinicId = clinicA.Id,
                PatientId = otherPatient.Id,
                ClinicPatientId = enrollment.Id,
                DoctorStaffMemberId = doctorId,
                AppointmentDateUtc = AlignedFutureSlotUtc(34),
                DurationMinutes = 30,
                Status = AppointmentStatus.Requested,
                Source = AppointmentSource.Patient,
                CreatedByUserId = Guid.NewGuid(),
                Version = 0,
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            otherAppointmentId = appt.Id;
        }

        var denied = await _client!.PostAsJsonAsync($"/api/v1/appointments/{otherAppointmentId}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(35),
            durationMinutes = 30,
            expectedVersion = 0,
        });
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        created.Should().NotBeNull();
    }

    [Fact]
    public async Task Staff_Reschedules_Within_Clinic()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var patientId = await GetSeedPatientIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(40),
            durationMinutes = 30,
        });
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created!.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(41),
            durationMinutes = 30,
            expectedVersion = created.Version,
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cross_Clinic_Reschedule_Denied()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var patientId = await GetSeedPatientIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(42),
            durationMinutes = 30,
        });
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        await AuthenticateAsync(StaffBEmail, StaffBPassword);
        var denied = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created!.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(43),
            durationMinutes = 30,
            expectedVersion = created.Version,
        });
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Terminal_State_Reschedule_Returns_409()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId, AlignedFutureSlotUtc(44));
        var cancel = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{created.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancel.Content.ReadFromJsonAsync<AppointmentResponse>();

        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(45),
            durationMinutes = 30,
            expectedVersion = cancelled!.Version,
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await reschedule.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("extensions").GetProperty("errorCode").GetString()
            .Should().Be(AppointmentErrorCodes.RescheduleNotAllowed);
    }

    [Fact]
    public async Task Valid_New_Slot_Succeeds_And_Identity_Unchanged()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId, AlignedFutureSlotUtc(50));
        var newStart = AlignedFutureSlotUtc(51);

        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = newStart,
            durationMinutes = 30,
            expectedVersion = created.Version,
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await reschedule.Content.ReadFromJsonAsync<AppointmentResponse>();
        body!.Id.Should().Be(created.Id);
        body.AppointmentDateUtc.Should().Be(newStart);
    }

    [Fact]
    public async Task Overlap_Returns_409()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var start = AlignedFutureSlotUtc(52);
        var first = await CreatePatientAppointmentAsync(doctorId, start);
        var second = await CreatePatientAppointmentAsync(doctorId, AlignedFutureSlotUtc(53));

        var conflict = await _client!.PostAsJsonAsync($"/api/v1/appointments/{second.Id}/reschedule", new
        {
            appointmentDateUtc = start,
            durationMinutes = 30,
            expectedVersion = second.Version,
        });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        first.Should().NotBeNull();
    }

    [Fact]
    public async Task Availability_Exception_Returns_409()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var exceptionDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(60);
        var createException = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/doctors/{doctorId}/availability-exceptions",
            new
            {
                date = exceptionDate.ToString("yyyy-MM-dd"),
                exceptionType = "UnavailableFullDay",
            });
        createException.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthenticateAsync(PatientEmail, PatientPassword);
        var created = await CreatePatientAppointmentAsync(doctorId, AlignedFutureSlotUtc(55));
        var blockedStart = new DateTimeOffset(
            exceptionDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified),
            TimeSpan.FromHours(3)).ToUniversalTime();

        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = blockedStart,
            durationMinutes = 30,
            expectedVersion = created.Version,
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Stale_Version_Returns_409()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId, AlignedFutureSlotUtc(56));

        var stale = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(57),
            durationMinutes = 30,
            expectedVersion = created.Version + 9,
        });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Reminder_Replacement_And_History_Saved()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var oldStart = AlignedFutureSlotUtc(70);
        var created = await CreatePatientAppointmentAsync(doctorId, oldStart);

        Guid oldUpcomingId;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var upcoming = await db.AppointmentReminders.SingleAsync(
                r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Upcoming);
            oldUpcomingId = upcoming.Id;
        }

        var newStart = AlignedFutureSlotUtc(71);
        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = newStart,
            durationMinutes = 30,
            expectedVersion = created.Version,
            reason = "Shift",
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope2 = _factory!.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var upcomings = await db2.AppointmentReminders
            .Where(r => r.AppointmentId == created.Id && r.ReminderType == AppointmentReminderType.Upcoming)
            .ToListAsync();
        upcomings.Should().ContainSingle();
        upcomings[0].Id.Should().Be(oldUpcomingId);
        upcomings[0].Status.Should().Be(AppointmentReminderStatus.Pending);
        upcomings[0].ScheduledAtUtc.Should().Be(newStart - TimeSpan.FromHours(24));

        var history = await db2.AppointmentRescheduleHistories.SingleAsync(h => h.AppointmentId == created.Id);
        history.PreviousStartUtc.Should().Be(oldStart);
        history.NewStartUtc.Should().Be(newStart);
        history.Reason.Should().Be("Shift");
    }

    private async Task<AppointmentResponse> CreatePatientAppointmentAsync(Guid doctorId, DateTimeOffset start)
    {
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = start,
            durationMinutes = 30,
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AppointmentResponse>())!;
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
