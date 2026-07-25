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

public sealed class ClinicAdminAppointmentEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string StaffBEmail = "doctor.b@healthcare.local";
    private const string StaffBPassword = "ChangeMe_DoctorB_1!";
    private const string ClinicAdminEmail = "clinicadmin@healthcare.local";
    private const string ClinicAdminPassword = "ChangeMe_ClinicAdmin_1!";
    private const string StaffAEmail = "doctor.a@healthcare.local";
    private const string StaffAPassword = "ChangeMe_DoctorA_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_clinic_admin_appointment_test")
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
    public async Task Clinic_Admin_Lists_Only_Own_Clinic_And_Can_No_Show()
    {
        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var me = await _client!.GetFromJsonAsync<CurrentUserResponse>("/api/v1/auth/me");
        var ownClinicId = me!.ClinicId!.Value;
        var patientId = await GetSeedPatientIdAsync();
        var doctorId = await GetClinicADoctorStaffIdAsync();

        var create = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead: 5),
            durationMinutes = 30,
            reason = "CA6-no-show",
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();
        created!.ClinicId.Should().Be(ownClinicId);
        created.Status.Should().Be("Confirmed");

        var queue = await _client!.GetFromJsonAsync<PagedResponse<AppointmentResponse>>(
            "/api/v1/staff/appointments/queue");
        queue!.Items.Should().OnlyContain(i => i.ClinicId == ownClinicId);
        queue.Items.Should().Contain(i => i.Id == created.Id);

        var detail = await _client!.GetAsync($"/api/v1/appointments/{created.Id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await detail.Content.ReadAsStringAsync();
        detailBody.ToLowerInvariant().Should().NotContain("medical_note");
        detailBody.ToLowerInvariant().Should().NotContain("diagnosis");

        var noShow = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created.Id}/no-show",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        noShow.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await noShow.Content.ReadFromJsonAsync<AppointmentResponse>();
        updated!.Status.Should().Be("NoShow");

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var notes = await db.MedicalNotes.CountAsync(n => n.AppointmentId == created.Id);
        notes.Should().Be(0);

        var audit = await db.OrganizationAuditEvents.AsNoTracking()
            .Where(e => e.Action == "appointment_no_show" || e.ResourceId == created.Id)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(5)
            .ToListAsync();
        audit.Should().NotBeEmpty();
        var json = System.Text.Json.JsonSerializer.Serialize(audit);
        json.ToLowerInvariant().Should().NotContain("password");
        json.ToLowerInvariant().Should().NotContain("token");
        json.ToLowerInvariant().Should().NotContain("secret");
    }

    [Fact]
    public async Task Clinic_Admin_Can_Complete_From_Checked_In()
    {
        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var patientId = await GetSeedPatientIdAsync();
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead: 6),
            durationMinutes = 30,
        });
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        var checkIn = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created!.Id}/check-in",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        checkIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkedIn = await checkIn.Content.ReadFromJsonAsync<AppointmentResponse>();

        var complete = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{checkedIn!.Id}/complete",
            new AppointmentActionRequest { ExpectedVersion = checkedIn.Version });
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await complete.Content.ReadFromJsonAsync<AppointmentResponse>())!.Status.Should().Be("Completed");

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        (await db.MedicalNotes.CountAsync(n => n.AppointmentId == created.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Clinic_Admin_Invalid_Transition_And_Stale_Version()
    {
        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var patientId = await GetSeedPatientIdAsync();
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var create = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead: 7),
            durationMinutes = 30,
        });
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        var invalid = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created!.Id}/complete",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        invalid.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var stale = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created.Id}/no-show",
            new AppointmentActionRequest { ExpectedVersion = 9999 });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cross_Clinic_Detail_Denied_For_Clinic_Admin()
    {
        Guid createdId;
        int version;
        using (var scope = _factory!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            var clinicB = await db.Clinics.SingleAsync(c => c.Slug == "dev-clinic-b");
            var doctorB = await db.StaffMembers
                .Where(s => s.ClinicId == clinicB.Id && s.Role == AppRoles.Doctor)
                .Select(s => s.Id)
                .SingleAsync();
            var patientId = await db.Patients.Select(p => p.Id).FirstAsync();
            var enrollment = await db.ClinicPatients
                .SingleOrDefaultAsync(cp => cp.ClinicId == clinicB.Id && cp.PatientId == patientId);
            if (enrollment is null)
            {
                enrollment = new Domain.Patients.ClinicPatient
                {
                    Id = Guid.NewGuid(),
                    ClinicId = clinicB.Id,
                    PatientId = patientId,
                    LocalPatientNumber = "B-CA",
                    Status = Domain.Patients.ClinicPatientStatus.Active,
                    RegisteredAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                db.ClinicPatients.Add(enrollment);
            }

            var appointment = new Domain.Appointments.Appointment
            {
                Id = Guid.NewGuid(),
                OrganizationId = clinicB.OrganizationId,
                ClinicId = clinicB.Id,
                PatientId = patientId,
                ClinicPatientId = enrollment.Id,
                DoctorStaffMemberId = doctorB,
                AppointmentDateUtc = AlignedFutureSlotUtc(daysAhead: 8),
                DurationMinutes = 30,
                Status = Domain.Appointments.AppointmentStatus.Confirmed,
                Source = Domain.Appointments.AppointmentSource.Staff,
                CreatedByUserId = Guid.NewGuid(),
                Version = 0,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            createdId = appointment.Id;
            version = appointment.Version;
        }

        await AuthenticateAsync(ClinicAdminEmail, ClinicAdminPassword);
        var detail = await _client!.GetAsync($"/api/v1/appointments/{createdId}");
        detail.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        var mutate = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{createdId}/no-show",
            new AppointmentActionRequest { ExpectedVersion = version });
        mutate.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patient_And_Anonymous_Denied_From_Staff_Appointment_Apis()
    {
        (await _client!.GetAsync("/api/v1/staff/appointments/queue")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        await AuthenticateAsync(PatientEmail, PatientPassword);
        (await _client!.GetAsync("/api/v1/staff/appointments/queue")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Bypass_For_Queue()
    {
        await AuthenticateAsync("admin@healthcare.local", "ChangeMe_Admin_1!");
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var clinicAId = await db.Clinics.Where(c => c.Slug == "dev-clinic-a").Select(c => c.Id).SingleAsync();

        var denied = await _client!.GetAsync($"/api/v1/staff/appointments/queue?clinicId={clinicAId:D}");
        denied.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);

        var allowed = await _client!.GetAsync(
            $"/api/v1/staff/appointments/queue?clinicId={clinicAId:D}&platformAdminBypass=true");
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await allowed.Content.ReadAsStringAsync();
        body.ToLowerInvariant().Should().NotContain("medical_note");
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
