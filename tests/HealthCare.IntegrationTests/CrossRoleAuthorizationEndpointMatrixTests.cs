using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.MedicalNotes;
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

/// <summary>
/// DR-9: HTTP cross-role negative matrix against seeded MVP endpoints.
/// Shared fixtures avoid per-case container startup.
/// </summary>
public sealed class CrossRoleAuthorizationEndpointMatrixTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string DoctorAEmail = "doctor.a@healthcare.local";
    private const string DoctorAPassword = "ChangeMe_DoctorA_1!";
    private const string DoctorBEmail = "doctor.b@healthcare.local";
    private const string DoctorBPassword = "ChangeMe_DoctorB_1!";
    private const string ClinicAdminEmail = "clinicadmin@healthcare.local";
    private const string ClinicAdminPassword = "ChangeMe_ClinicAdmin_1!";
    private const string OrgAdminEmail = "orgadmin@healthcare.local";
    private const string OrgAdminPassword = "ChangeMe_OrgAdmin_1!";
    private const string PlatformAdminEmail = "admin@healthcare.local";
    private const string PlatformAdminPassword = "ChangeMe_Admin_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private Guid _doctorAAppointmentId;
    private int _doctorAAppointmentVersion;
    private Guid _doctorANoteId;
    private Guid _clinicBAppointmentId;
    private Guid _foreignPatientId;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_dr9_authz_matrix_test")
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

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            IntegrationTestHost.ApplyDefaultSettings(builder);
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Jwt:Issuer", "HealthCare");
            builder.UseSetting("Jwt:Audience", "HealthCare");
            builder.UseSetting("Jwt:SigningKey", "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+");
            builder.UseSetting("DevelopmentSeed:Admin:Email", PlatformAdminEmail);
            builder.UseSetting("DevelopmentSeed:Admin:Password", PlatformAdminPassword);
            builder.UseSetting("DevelopmentSeed:Patient:Email", PatientEmail);
            builder.UseSetting("DevelopmentSeed:Patient:Password", PatientPassword);
            builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", DoctorAEmail);
            builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", DoctorAPassword);
            builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffEmail", DoctorBEmail);
            builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffPassword", DoctorBPassword);
            builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminEmail", OrgAdminEmail);
            builder.UseSetting("DevelopmentSeed:Patient:OrganizationAdminPassword", OrgAdminPassword);
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
        await SeedMatrixFixturesAsync();
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
    public async Task Http_Negative_Matrix_Returns_Documented_Status_Codes()
    {
        var cases = BuildCases();
        var failures = new List<string>();

        foreach (var testCase in cases)
        {
            try
            {
                await ExecuteAsync(testCase);
            }
            catch (Exception ex)
            {
                failures.Add($"{testCase.Name}: {ex.Message}");
            }
        }

        failures.Should().BeEmpty(
            because: "every DR-9 HTTP matrix case must match documented 401/403/404 semantics");
    }

    [Fact]
    public async Task Out_Of_Scope_Complete_Does_Not_Mutate_Peer_Appointment()
    {
        await AuthenticateAsync(DoctorBEmail, DoctorBPassword);
        var before = await GetAppointmentAsync(_doctorAAppointmentId);
        before.Should().BeNull("cross-clinic doctor must not retrieve peer appointment detail");

        var response = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{_doctorAAppointmentId}/complete",
            new AppointmentActionRequest { ExpectedVersion = 9999 });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await AuthenticateAsync(DoctorAEmail, DoctorAPassword);
        var after = await _client!.GetFromJsonAsync<AppointmentResponse>(
            $"/api/v1/appointments/{_doctorAAppointmentId}");
        after!.Status.Should().Be("CheckedIn");
        after.Version.Should().Be(_doctorAAppointmentVersion);
    }

    private IReadOnlyList<MatrixCase> BuildCases() =>
    [
        new("anon_staff_patients", null, HttpMethod.Get, "/api/v1/staff/patients", null, HttpStatusCode.Unauthorized),
        new("anon_complete", null, HttpMethod.Post, $"/api/v1/staff/appointments/{_doctorAAppointmentId}/complete",
            Json(new AppointmentActionRequest { ExpectedVersion = _doctorAAppointmentVersion }), HttpStatusCode.Unauthorized),
        new("anon_note_detail", null, HttpMethod.Get, $"/api/v1/medical-notes/{_doctorANoteId}", null, HttpStatusCode.Unauthorized),
        new("anon_clinic_reports", null, HttpMethod.Get, "/api/v1/clinic/reports/appointments?fromDate=2026-01-01&toDate=2026-01-07", null, HttpStatusCode.Unauthorized),

        new("patient_staff_patients", PatientEmail, HttpMethod.Get, "/api/v1/staff/patients", null, HttpStatusCode.Forbidden),
        new("patient_complete", PatientEmail, HttpMethod.Post, $"/api/v1/staff/appointments/{_doctorAAppointmentId}/complete",
            Json(new AppointmentActionRequest { ExpectedVersion = _doctorAAppointmentVersion }), HttpStatusCode.Forbidden),
        new("patient_note_detail", PatientEmail, HttpMethod.Get, $"/api/v1/medical-notes/{_doctorANoteId}", null, HttpStatusCode.Forbidden),
        new("patient_clinic_reports", PatientEmail, HttpMethod.Get, "/api/v1/clinic/reports/appointments?fromDate=2026-01-01&toDate=2026-01-07", null, HttpStatusCode.Forbidden),
        new("patient_clinic_audit", PatientEmail, HttpMethod.Get, "/api/v1/clinic/audit-logs", null, HttpStatusCode.Forbidden),
        new("patient_doctor_dashboard", PatientEmail, HttpMethod.Get, "/api/v1/doctor/dashboard", null, HttpStatusCode.Forbidden),
        new("patient_foreign_patient_detail", PatientEmail, HttpMethod.Get, $"/api/v1/staff/patients/{_foreignPatientId}", null, HttpStatusCode.Forbidden),

        new("doctor_clinic_reports", DoctorAEmail, HttpMethod.Get, "/api/v1/clinic/reports/appointments?fromDate=2026-01-01&toDate=2026-01-07", null, HttpStatusCode.Forbidden),
        new("doctor_clinic_audit", DoctorAEmail, HttpMethod.Get, "/api/v1/clinic/audit-logs", null, HttpStatusCode.Forbidden),
        new("doctor_org_dashboard", DoctorAEmail, HttpMethod.Get, "/api/v1/organization/dashboard", null, HttpStatusCode.Forbidden),
        new("doctor_cross_clinic_appointment", DoctorAEmail, HttpMethod.Get, $"/api/v1/appointments/{_clinicBAppointmentId}", null, HttpStatusCode.NotFound),
        new("doctor_cross_clinic_complete", DoctorAEmail, HttpMethod.Post, $"/api/v1/staff/appointments/{_clinicBAppointmentId}/complete",
            Json(new AppointmentActionRequest { ExpectedVersion = 0 }), HttpStatusCode.NotFound),
        new("doctor_b_peer_note", DoctorBEmail, HttpMethod.Get, $"/api/v1/medical-notes/{_doctorANoteId}", null, HttpStatusCode.NotFound),
        new("doctor_b_peer_note_list", DoctorBEmail, HttpMethod.Get, $"/api/v1/appointments/{_doctorAAppointmentId}/medical-notes", null, HttpStatusCode.NotFound),
        new("doctor_b_peer_complete", DoctorBEmail, HttpMethod.Post, $"/api/v1/staff/appointments/{_doctorAAppointmentId}/complete",
            Json(new AppointmentActionRequest { ExpectedVersion = _doctorAAppointmentVersion }), HttpStatusCode.NotFound),

        new("clinic_admin_note_body", ClinicAdminEmail, HttpMethod.Get, $"/api/v1/medical-notes/{_doctorANoteId}", null, HttpStatusCode.Forbidden),
        new("org_admin_note_body", OrgAdminEmail, HttpMethod.Get, $"/api/v1/medical-notes/{_doctorANoteId}", null, HttpStatusCode.Forbidden),
        new("platform_admin_note_body", PlatformAdminEmail, HttpMethod.Get, $"/api/v1/medical-notes/{_doctorANoteId}", null, HttpStatusCode.Forbidden),
        new("platform_admin_note_bypass_still_denied", PlatformAdminEmail, HttpMethod.Get,
            $"/api/v1/medical-notes/{_doctorANoteId}?platformAdminBypass=true", null, HttpStatusCode.Forbidden),

        new("clinic_admin_cross_clinic_appointment", ClinicAdminEmail, HttpMethod.Get, $"/api/v1/appointments/{_clinicBAppointmentId}", null,
            HttpStatusCode.NotFound, AllowForbidden: true),
    ];

    private async Task ExecuteAsync(MatrixCase testCase)
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        if (testCase.ActorEmail is not null)
        {
            var password = PasswordFor(testCase.ActorEmail);
            await AuthenticateAsync(testCase.ActorEmail, password);
        }

        using var request = new HttpRequestMessage(testCase.Method, testCase.Path);
        if (testCase.JsonBody is not null)
        {
            request.Content = new StringContent(testCase.JsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await _client!.SendAsync(request);
        if (testCase.AllowForbidden && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return;
        }

        response.StatusCode.Should().Be(testCase.Expected, because: testCase.Name);
    }

    private async Task SeedMatrixFixturesAsync()
    {
        var doctorAAppt = await CreateCheckedInAppointmentAsync(DoctorAEmail, DoctorAPassword, "dev-clinic-a", daysAhead: 40);
        _doctorAAppointmentId = doctorAAppt.Id;
        _doctorAAppointmentVersion = doctorAAppt.Version;

        await AuthenticateAsync(DoctorAEmail, DoctorAPassword);
        var noteCreate = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{_doctorAAppointmentId}/medical-notes",
            new CreateMedicalNoteDraftRequest { NoteType = "Progress", Plan = "DR9 plan" });
        noteCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        var note = (await noteCreate.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!;
        _doctorANoteId = note.Id;

        var clinicBAppt = await CreateCheckedInAppointmentAsync(DoctorBEmail, DoctorBPassword, "dev-clinic-b", daysAhead: 41);
        _clinicBAppointmentId = clinicBAppt.Id;

        _foreignPatientId = await SeedForeignPatientAsync();
    }

    private async Task<AppointmentResponse> CreateCheckedInAppointmentAsync(
        string doctorEmail,
        string doctorPassword,
        string clinicSlug,
        int daysAhead)
    {
        await AuthenticateAsync(PatientEmail, PatientPassword);
        var doctorId = await GetDoctorIdAsync(clinicSlug);
        var create = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode = clinicSlug,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead),
            durationMinutes = 30,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var appointment = (await create.Content.ReadFromJsonAsync<AppointmentResponse>())!;

        await AuthenticateAsync(doctorEmail, doctorPassword);
        var confirm = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{appointment.Id}/confirm",
            new AppointmentActionRequest { ExpectedVersion = appointment.Version });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        appointment = (await confirm.Content.ReadFromJsonAsync<AppointmentResponse>())!;

        var checkIn = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{appointment.Id}/check-in",
            new AppointmentActionRequest { ExpectedVersion = appointment.Version });
        checkIn.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await checkIn.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    private async Task<Guid> SeedForeignPatientAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var clinic = await db.Clinics.SingleAsync(c => c.Slug == "dev-clinic-a");
        var patientId = Guid.NewGuid();
        db.Patients.Add(new Domain.Patients.Patient
        {
            Id = patientId,
            FirstName = "Foreign",
            LastName = "Patient",
            IsActive = true,
        });
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinic.Id,
            PatientId = patientId,
            LocalPatientNumber = "DR9-FOREIGN",
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return patientId;
    }

    private async Task<AppointmentResponse?> GetAppointmentAsync(Guid appointmentId)
    {
        var response = await _client!.GetAsync($"/api/v1/appointments/{appointmentId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppointmentResponse>();
    }

    private async Task<Guid> GetDoctorIdAsync(string clinicSlug)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        return await db.StaffMembers.Where(s => s.Role == AppRoles.Doctor)
            .Join(db.Clinics.Where(c => c.Slug == clinicSlug), s => s.ClinicId, c => c.Id, (s, _) => s.Id)
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
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }

    private static string PasswordFor(string email) => email switch
    {
        PatientEmail => PatientPassword,
        DoctorAEmail => DoctorAPassword,
        DoctorBEmail => DoctorBPassword,
        ClinicAdminEmail => ClinicAdminPassword,
        OrgAdminEmail => OrgAdminPassword,
        PlatformAdminEmail => PlatformAdminPassword,
        _ => throw new InvalidOperationException($"Unknown actor {email}"),
    };

    private static string Json(object body) => JsonSerializer.Serialize(body);

    private static DateTimeOffset AlignedFutureSlotUtc(int daysAhead)
    {
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysAhead));
        while (localDate.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
        {
            localDate = localDate.AddDays(1);
        }

        var local = localDate.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Unspecified);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Arab Standard Time" : "Asia/Riyadh");
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private sealed record MatrixCase(
        string Name,
        string? ActorEmail,
        HttpMethod Method,
        string Path,
        string? JsonBody,
        HttpStatusCode Expected,
        bool AllowForbidden = false);
}
