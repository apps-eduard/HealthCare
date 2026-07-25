using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

/// <summary>PM-1 HTTP coverage: cutoff boundaries and authz-before-conflict.</summary>
public sealed class PatientContractHardeningEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string Patient2Email = "patient.pm1.other@healthcare.local";
    private const string Patient2Password = "ChangeMe_Patient2_1!";
    private const string StaffAEmail = "doctor.a@healthcare.local";
    private const string StaffAPassword = "ChangeMe_DoctorA_1!";
    private const string ClinicAdminEmail = "clinicadmin@healthcare.local";
    private const string ClinicAdminPassword = "ChangeMe_ClinicAdmin_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private Guid _foreignOrgPatientId;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_patient_pm1_test")
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
        await SeedExtraPatientsAsync();
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
    public async Task Cancel_Exactly_At_Two_Hour_Cutoff_Succeeds()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId);
        await SetAppointmentStartAsync(created.Id, DateTimeOffset.UtcNow.Add(AppointmentService.PatientScheduleMutationCutoff));

        var cancel = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/cancel", new
        {
            expectedVersion = created.Version,
        });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await cancel.Content.ReadFromJsonAsync<AppointmentResponse>();
        body!.Status.Should().Be(nameof(AppointmentStatus.CancelledByPatient));
    }

    [Fact]
    public async Task Cancel_Inside_Two_Hour_Cutoff_Returns_409()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId);
        await SetAppointmentStartAsync(created.Id, DateTimeOffset.UtcNow.AddHours(1));

        var cancel = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/cancel", new
        {
            expectedVersion = created.Version,
        });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await cancel.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be(AppointmentErrorCodes.PatientMutationCutoff);

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == created.Id))
            .Status.Should().Be(AppointmentStatus.Requested);
    }

    [Fact]
    public async Task Reschedule_Outside_Cutoff_Succeeds_And_Inside_Fails()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId);

        var ok = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedSlotDaysAhead(6),
            durationMinutes = 30,
            expectedVersion = created.Version,
        });
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await ok.Content.ReadFromJsonAsync<AppointmentResponse>();

        await SetAppointmentStartAsync(moved!.Id, DateTimeOffset.UtcNow.AddMinutes(45));
        var denied = await _client!.PostAsJsonAsync($"/api/v1/appointments/{moved.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedSlotDaysAhead(7),
            durationMinutes = 30,
            expectedVersion = moved.Version,
        });
        denied.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await denied.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be(AppointmentErrorCodes.PatientMutationCutoff);
    }

    [Fact]
    public async Task Reschedule_Exactly_At_Two_Hour_Cutoff_Succeeds()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId);
        await SetAppointmentStartAsync(created.Id, DateTimeOffset.UtcNow.Add(AppointmentService.PatientScheduleMutationCutoff));

        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = AlignedSlotDaysAhead(9),
            durationMinutes = 30,
            expectedVersion = created.Version,
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Foreign_Appointment_Cancel_With_Stale_Version_Returns_404()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId);

        await AuthenticateAsync(Patient2Email, Patient2Password);
        var cancel = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/cancel", new
        {
            expectedVersion = 0,
        });
        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Foreign_Appointment_Reschedule_With_Bad_Slot_Returns_404()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        var created = await CreatePatientAppointmentAsync(doctorId);

        await AuthenticateAsync(Patient2Email, Patient2Password);
        var reschedule = await _client!.PostAsJsonAsync($"/api/v1/appointments/{created.Id}/reschedule", new
        {
            appointmentDateUtc = DateTimeOffset.UtcNow.AddHours(-1),
            durationMinutes = 30,
            expectedVersion = 0,
        });
        reschedule.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Foreign_Organization_Patient_Profile_Returns_404()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var response = await _client!.GetAsync($"/api/v1/patients/{_foreignOrgPatientId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Patient_Appointment_List_Omits_Staff_Display_Helpers()
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetClinicADoctorStaffIdAsync();
        await CreatePatientAppointmentAsync(doctorId);

        var list = await _client!.GetFromJsonAsync<PagedResponse<AppointmentResponse>>("/api/v1/patients/me/appointments");
        var item = list!.Items.Should().NotBeEmpty().And.Subject.First();
        item.PatientDisplayName.Should().BeNull();
        item.LocalPatientNumber.Should().BeNull();
        item.ClinicName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Staff_Still_Receives_403_On_Patients_Me()
    {
        await AuthenticateAsync(StaffAEmail, StaffAPassword);
        var response = await _client!.GetAsync("/api/v1/patients/me");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_Patients_By_Id_Returns_401()
    {
        var response = await _client!.GetAsync($"/api/v1/patients/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task SeedExtraPatientsAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;

        var clinicA = await db.Clinics.SingleAsync(c => c.Slug == "dev-clinic-a");

        var user2 = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = Patient2Email,
            UserName = Patient2Email,
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user2, Patient2Password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user2, AppRoles.Patient);

        var patient2 = new Domain.Patients.Patient
        {
            Id = Guid.NewGuid(),
            UserId = user2.Id,
            FirstName = "Other",
            LastName = "Linked",
            IsActive = true,
        };
        db.Patients.Add(patient2);
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicA.Id,
            PatientId = patient2.Id,
            LocalPatientNumber = "A-PM1-2",
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = now,
            UpdatedAtUtc = now,
        });

        var foreignOrg = Guid.NewGuid();
        var foreignClinic = Guid.NewGuid();
        db.Organizations.Add(new Domain.Organizations.Organization
        {
            Id = foreignOrg,
            Name = "Foreign Org PM1",
            Slug = "foreign-org-pm1",
            Status = Domain.Organizations.OrganizationStatus.Active,
        });
        db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = foreignClinic,
            OrganizationId = foreignOrg,
            Name = "Foreign Clinic",
            Slug = "foreign-clinic-pm1",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        });
        var foreignPatient = new Domain.Patients.Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Foreign",
            LastName = "Patient",
            IsActive = true,
        };
        db.Patients.Add(foreignPatient);
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = foreignClinic,
            PatientId = foreignPatient.Id,
            LocalPatientNumber = "F-1",
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = now,
            UpdatedAtUtc = now,
        });
        _foreignOrgPatientId = foreignPatient.Id;
        await db.SaveChangesAsync();
    }

    private async Task SetAppointmentStartAsync(Guid appointmentId, DateTimeOffset start)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var appt = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        appt.AppointmentDateUtc = start;
        await db.SaveChangesAsync();
    }

    private async Task<AppointmentResponse> CreatePatientAppointmentAsync(Guid doctorId)
    {
        var bookAt = AlignedSlotDaysAhead(30 + Random.Shared.Next(1, 50));
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = bookAt,
            durationMinutes = 30,
            reason = "PM-1",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    private static DateTimeOffset AlignedSlotDaysAhead(int daysAhead)
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
            .FirstAsync();
    }

    private async Task AuthenticateAsync(string email, string password)
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
