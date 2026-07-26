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
using HealthCare.Domain.Staff;
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

/// <summary>
/// PM-7: Patient-centered HTTP security negative matrix.
/// Complements DR-9 (Doctor-focused) and PM-1 cutoff/hardening Facts without replacing them.
/// </summary>
public sealed class PatientSecurityEndpointMatrixTests : IAsyncLifetime
{
    private const string PatientAEmail = "patient@healthcare.local";
    private const string PatientAPassword = "ChangeMe_Patient_1!";
    private const string PatientBEmail = "patient.pm7.b@healthcare.local";
    private const string PatientBPassword = "ChangeMe_PatientB_1!";
    private const string UnlinkedEmail = "patient.pm7.unlinked@healthcare.local";
    private const string UnlinkedPassword = "ChangeMe_Unlinked_1!";
    private const string InactiveEmail = "patient.pm7.inactive@healthcare.local";
    private const string InactivePassword = "ChangeMe_Inactive_1!";
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
    private const string NurseEmail = "nurse.pm7@healthcare.local";
    private const string NursePassword = "ChangeMe_Nurse_1!";
    private const string ReceptionistEmail = "recv.pm7@healthcare.local";
    private const string ReceptionistPassword = "ChangeMe_Recv_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    private Guid _patientAAppointmentId;
    private int _patientAAppointmentVersion;
    private Guid _patientBAppointmentId;
    private Guid _foreignOrgPatientId;
    private Guid _unknownPatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private Guid _noteId;
    private Guid _doctorAStaffId;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_patient_pm7_matrix_test")
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
            builder.UseSetting("DevelopmentSeed:Patient:Email", PatientAEmail);
            builder.UseSetting("DevelopmentSeed:Patient:Password", PatientAPassword);
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
    public async Task Http_Patient_Security_Matrix_Returns_Documented_Status_Codes()
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
            because: "every PM-7 Patient HTTP matrix case must match documented 401/403/404 semantics");
    }

    [Fact]
    public async Task Foreign_Appointment_Detail_And_Authz_Before_Conflict_Do_Not_Mutate()
    {
        await AuthenticateAsync(PatientBEmail, PatientBPassword);
        var detail = await _client!.GetAsync($"/api/v1/appointments/{_patientAAppointmentId}");
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var cancel = await _client.PostAsJsonAsync(
            $"/api/v1/appointments/{_patientAAppointmentId}/cancel",
            new AppointmentActionRequest { ExpectedVersion = 0 });
        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var reschedule = await _client.PostAsJsonAsync(
            $"/api/v1/appointments/{_patientAAppointmentId}/reschedule",
            new
            {
                appointmentDateUtc = AlignedFutureSlotUtc(20),
                durationMinutes = 30,
                expectedVersion = 0,
            });
        reschedule.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await AuthenticateAsync(PatientAEmail, PatientAPassword);
        var after = await _client.GetFromJsonAsync<AppointmentResponse>(
            $"/api/v1/appointments/{_patientAAppointmentId}");
        after!.Status.Should().Be("Requested");
        after.Version.Should().Be(_patientAAppointmentVersion);
        after.Id.Should().Be(_patientAAppointmentId);
    }

    [Fact]
    public async Task Patient_Responses_Omit_Staff_Display_And_Clinical_Fields()
    {
        await AuthenticateAsync(PatientAEmail, PatientAPassword);

        using var listResponse = await _client!.GetAsync("/api/v1/patients/me/appointments");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertPatientSafeAppointmentJsonAsync(await listResponse.Content.ReadAsStringAsync());

        using var detailResponse = await _client.GetAsync($"/api/v1/appointments/{_patientAAppointmentId}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertPatientSafeAppointmentJsonAsync(await detailResponse.Content.ReadAsStringAsync());

        using var profileResponse = await _client.GetAsync("/api/v1/patients/me");
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var profileJson = await profileResponse.Content.ReadAsStringAsync();
        profileJson.ToLowerInvariant().Should().NotContain("medicalnote");
        profileJson.ToLowerInvariant().Should().NotContain("diagnosis");
        profileJson.Should().NotContain("LocalPatientNumber");

        using var clinicsResponse = await _client.GetAsync("/api/v1/patients/me/clinics");
        clinicsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var clinicsJson = await clinicsResponse.Content.ReadAsStringAsync();
        clinicsJson.ToLowerInvariant().Should().NotContain("organizationid");
        clinicsJson.Should().NotContain("\"clinicId\"");
        clinicsJson.Should().NotContain("\"ClinicId\"");

        using var doctorsResponse = await _client.GetAsync("/api/v1/clinics/dev-clinic-a/doctors");
        doctorsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var doctorsJson = (await doctorsResponse.Content.ReadAsStringAsync()).ToLowerInvariant();
        doctorsJson.Should().NotContain("\"email\"");
        doctorsJson.Should().NotContain("\"phone\"");
        doctorsJson.Should().NotContain("userid");
    }

    [Fact]
    public async Task Foreign_And_Unknown_Patient_Profile_Return_Equivalent_404()
    {
        await AuthenticateAsync(PatientAEmail, PatientAPassword);
        using var foreign = await _client!.GetAsync($"/api/v1/patients/{_foreignOrgPatientId}");
        using var unknown = await _client.GetAsync($"/api/v1/patients/{_unknownPatientId}");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var foreignBody = await foreign.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        foreignBody.Should().NotContain(_foreignOrgPatientId.ToString());
        unknownBody.Should().NotContain(_unknownPatientId.ToString());
    }

    private IReadOnlyList<MatrixCase> BuildCases()
    {
        var cancelBody = Json(new AppointmentActionRequest { ExpectedVersion = 0 });
        var rescheduleBody = Json(new
        {
            appointmentDateUtc = AlignedFutureSlotUtc(25),
            durationMinutes = 30,
            expectedVersion = 0,
        });
        var bookBody = Json(new
        {
            clinicCode = "dev-clinic-a",
            doctorStaffMemberId = _doctorAStaffId,
            appointmentDateUtc = AlignedFutureSlotUtc(30),
            durationMinutes = 30,
        });

        return
        [
            // Anonymous → 401
            new("anon_profile", null, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Unauthorized),
            new("anon_clinics", null, HttpMethod.Get, "/api/v1/patients/me/clinics", null, HttpStatusCode.Unauthorized),
            new("anon_book", null, HttpMethod.Post, "/api/v1/patients/me/appointments", bookBody, HttpStatusCode.Unauthorized),
            new("anon_list", null, HttpMethod.Get, "/api/v1/patients/me/appointments", null, HttpStatusCode.Unauthorized),
            new("anon_detail", null, HttpMethod.Get, $"/api/v1/appointments/{_patientAAppointmentId}", null, HttpStatusCode.Unauthorized),
            new("anon_cancel", null, HttpMethod.Post, $"/api/v1/appointments/{_patientAAppointmentId}/cancel", cancelBody, HttpStatusCode.Unauthorized),
            new("anon_reschedule", null, HttpMethod.Post, $"/api/v1/appointments/{_patientAAppointmentId}/reschedule", rescheduleBody, HttpStatusCode.Unauthorized),
            new("anon_doctors", null, HttpMethod.Get, "/api/v1/clinics/dev-clinic-a/doctors", null, HttpStatusCode.Unauthorized),
            new("anon_note", null, HttpMethod.Get, $"/api/v1/medical-notes/{_noteId}", null, HttpStatusCode.Unauthorized),
            new("anon_auth_me", null, HttpMethod.Get, "/api/v1/auth/me", null, HttpStatusCode.Unauthorized),

            // Cross-patient concealment → 404
            new("patientB_foreign_detail", PatientBEmail, HttpMethod.Get, $"/api/v1/appointments/{_patientAAppointmentId}", null, HttpStatusCode.NotFound),
            new("patientB_foreign_cancel", PatientBEmail, HttpMethod.Post, $"/api/v1/appointments/{_patientAAppointmentId}/cancel", cancelBody, HttpStatusCode.NotFound),
            new("patientB_foreign_reschedule", PatientBEmail, HttpMethod.Post, $"/api/v1/appointments/{_patientAAppointmentId}/reschedule", rescheduleBody, HttpStatusCode.NotFound),
            new("patientA_foreign_detail", PatientAEmail, HttpMethod.Get, $"/api/v1/appointments/{_patientBAppointmentId}", null, HttpStatusCode.NotFound),
            new("patientA_foreign_profile", PatientAEmail, HttpMethod.Get, $"/api/v1/patients/{_foreignOrgPatientId}", null, HttpStatusCode.NotFound),
            new("patientA_unknown_profile", PatientAEmail, HttpMethod.Get, $"/api/v1/patients/{_unknownPatientId}", null, HttpStatusCode.NotFound),

            // Patient → staff/clinical → 403
            new("patient_staff_patients", PatientAEmail, HttpMethod.Get, "/api/v1/staff/patients", null, HttpStatusCode.Forbidden),
            new("patient_staff_queue", PatientAEmail, HttpMethod.Get, "/api/v1/staff/appointments/queue", null, HttpStatusCode.Forbidden),
            new("patient_confirm", PatientAEmail, HttpMethod.Post, $"/api/v1/staff/appointments/{_patientAAppointmentId}/confirm", cancelBody, HttpStatusCode.Forbidden),
            new("patient_checkin", PatientAEmail, HttpMethod.Post, $"/api/v1/staff/appointments/{_patientAAppointmentId}/check-in", cancelBody, HttpStatusCode.Forbidden),
            new("patient_complete", PatientAEmail, HttpMethod.Post, $"/api/v1/staff/appointments/{_patientAAppointmentId}/complete", cancelBody, HttpStatusCode.Forbidden),
            new("patient_noshow", PatientAEmail, HttpMethod.Post, $"/api/v1/staff/appointments/{_patientAAppointmentId}/no-show", cancelBody, HttpStatusCode.Forbidden),
            new("patient_note_detail", PatientAEmail, HttpMethod.Get, $"/api/v1/medical-notes/{_noteId}", null, HttpStatusCode.Forbidden),
            new("patient_note_list", PatientAEmail, HttpMethod.Get, $"/api/v1/appointments/{_patientAAppointmentId}/medical-notes", null, HttpStatusCode.Forbidden),
            new("patient_clinic_reports", PatientAEmail, HttpMethod.Get, "/api/v1/clinic/reports/appointments?fromDate=2026-01-01&toDate=2026-01-07", null, HttpStatusCode.Forbidden),
            new("patient_clinic_audit", PatientAEmail, HttpMethod.Get, "/api/v1/clinic/audit-logs", null, HttpStatusCode.Forbidden),
            new("patient_clinic_dashboard", PatientAEmail, HttpMethod.Get, "/api/v1/clinic/dashboard", null, HttpStatusCode.Forbidden),
            new("patient_doctor_dashboard", PatientAEmail, HttpMethod.Get, "/api/v1/doctor/dashboard", null, HttpStatusCode.Forbidden),
            new("patient_org_dashboard", PatientAEmail, HttpMethod.Get, "/api/v1/organization/dashboard", null, HttpStatusCode.Forbidden),
            new("patient_staff_directory", PatientAEmail, HttpMethod.Get, $"/api/v1/staff/clinics/{Guid.Empty}/doctors", null, HttpStatusCode.Forbidden),

            // Wrong role / unlinked / inactive on Patient self-service → 403
            new("doctor_patients_me", DoctorAEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),
            new("clinic_admin_patients_me", ClinicAdminEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),
            new("org_admin_patients_me", OrgAdminEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),
            new("platform_admin_patients_me", PlatformAdminEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),
            new("nurse_patients_me", NurseEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),
            new("receptionist_patients_me", ReceptionistEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),
            new("unlinked_patients_me", UnlinkedEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),
            new("unlinked_book", UnlinkedEmail, HttpMethod.Post, "/api/v1/patients/me/appointments", bookBody, HttpStatusCode.Forbidden),
            new("inactive_patients_me", InactiveEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.Forbidden),

            // Positive smoke (authorized Patient A)
            new("patientA_profile_ok", PatientAEmail, HttpMethod.Get, "/api/v1/patients/me", null, HttpStatusCode.OK),
            new("patientA_list_ok", PatientAEmail, HttpMethod.Get, "/api/v1/patients/me/appointments", null, HttpStatusCode.OK),
            new("patientA_detail_ok", PatientAEmail, HttpMethod.Get, $"/api/v1/appointments/{_patientAAppointmentId}", null, HttpStatusCode.OK),
            new("patientA_clinics_ok", PatientAEmail, HttpMethod.Get, "/api/v1/patients/me/clinics", null, HttpStatusCode.OK),
            new("patientA_doctors_ok", PatientAEmail, HttpMethod.Get, "/api/v1/clinics/dev-clinic-a/doctors", null, HttpStatusCode.OK),
        ];
    }

    private async Task ExecuteAsync(MatrixCase testCase)
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        if (testCase.ActorEmail is not null)
        {
            await AuthenticateAsync(testCase.ActorEmail, PasswordFor(testCase.ActorEmail));
        }

        using var request = new HttpRequestMessage(testCase.Method, testCase.Path);
        if (testCase.JsonBody is not null)
        {
            request.Content = new StringContent(testCase.JsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await _client!.SendAsync(request);
        response.StatusCode.Should().Be(testCase.Expected, because: testCase.Name);

        if (testCase.Expected == HttpStatusCode.NotFound)
        {
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain("PatientDisplayName");
            body.Should().NotContain("LocalPatientNumber");
            body.Should().NotContain("slot_conflict");
            body.Should().NotContain("patient_mutation_cutoff");
            body.Should().NotContain("concurrency_conflict");
        }
    }

    private async Task SeedMatrixFixturesAsync()
    {
        _doctorAStaffId = await GetDoctorIdAsync("dev-clinic-a");
        await SeedExtraActorsAsync();

        await AuthenticateAsync(PatientAEmail, PatientAPassword);
        var patientAAppt = await CreatePatientAppointmentAsync("dev-clinic-a", _doctorAStaffId, daysAhead: 40);
        _patientAAppointmentId = patientAAppt.Id;
        _patientAAppointmentVersion = patientAAppt.Version;

        await AuthenticateAsync(PatientBEmail, PatientBPassword);
        var patientBAppt = await CreatePatientAppointmentAsync("dev-clinic-a", _doctorAStaffId, daysAhead: 41);
        _patientBAppointmentId = patientBAppt.Id;

        // Clinical note on a CheckedIn appointment owned via Patient A booking path (Doctor creates note).
        var checkedIn = await CreateCheckedInAppointmentAsync(daysAhead: 42);
        await AuthenticateAsync(DoctorAEmail, DoctorAPassword);
        var noteCreate = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{checkedIn.Id}/medical-notes",
            new CreateMedicalNoteDraftRequest { NoteType = "Progress", Plan = "PM7 plan" });
        noteCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        _noteId = (await noteCreate.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!.Id;

        _foreignOrgPatientId = await SeedForeignOrgPatientAsync();
    }

    private async Task SeedExtraActorsAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.UtcNow;
        var clinicA = await db.Clinics.SingleAsync(c => c.Slug == "dev-clinic-a");

        await CreateLinkedPatientAsync(users, db, clinicA.Id, PatientBEmail, PatientBPassword, "PM7", "PatientB", "A-PM7-B");
        await CreateUnlinkedPatientUserAsync(users, UnlinkedEmail, UnlinkedPassword);
        await CreateInactiveLinkedPatientAsync(users, db, clinicA.Id, InactiveEmail, InactivePassword);
        await CreateClinicStaffAsync(users, db, clinicA, NurseEmail, NursePassword, AppRoles.Nurse, "Nurse", "PM7");
        await CreateClinicStaffAsync(users, db, clinicA, ReceptionistEmail, ReceptionistPassword, AppRoles.Receptionist, "Recv", "PM7");
        await db.SaveChangesAsync();
    }

    private static async Task CreateLinkedPatientAsync(
        UserManager<ApplicationUser> users,
        HealthCareDbContext db,
        Guid clinicId,
        string email,
        string password,
        string first,
        string last,
        string localNumber)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, AppRoles.Patient);

        var patient = new Domain.Patients.Patient
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FirstName = first,
            LastName = last,
            IsActive = true,
        };
        db.Patients.Add(patient);
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patient.Id,
            LocalPatientNumber = localNumber,
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static async Task CreateUnlinkedPatientUserAsync(
        UserManager<ApplicationUser> users,
        string email,
        string password)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, AppRoles.Patient);
    }

    private static async Task CreateInactiveLinkedPatientAsync(
        UserManager<ApplicationUser> users,
        HealthCareDbContext db,
        Guid clinicId,
        string email,
        string password)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, AppRoles.Patient);

        var patient = new Domain.Patients.Patient
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FirstName = "Inactive",
            LastName = "Patient",
            IsActive = false,
        };
        db.Patients.Add(patient);
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patient.Id,
            LocalPatientNumber = "A-PM7-INACT",
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
    }

    private static async Task CreateClinicStaffAsync(
        UserManager<ApplicationUser> users,
        HealthCareDbContext db,
        Domain.Clinics.Clinic clinic,
        string email,
        string password,
        string role,
        string first,
        string last)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
        };
        (await users.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, role);

        db.StaffMembers.Add(new StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrganizationId = clinic.OrganizationId,
            ClinicId = clinic.Id,
            Role = role,
            FirstName = first,
            LastName = last,
            IsActive = true,
        });
    }

    private async Task<Guid> SeedForeignOrgPatientAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var foreignOrg = Guid.NewGuid();
        var foreignClinic = Guid.NewGuid();
        db.Organizations.Add(new Domain.Organizations.Organization
        {
            Id = foreignOrg,
            Name = "Foreign Org PM7",
            Slug = "foreign-org-pm7",
            Status = Domain.Organizations.OrganizationStatus.Active,
        });
        db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = foreignClinic,
            OrganizationId = foreignOrg,
            Name = "Foreign Clinic PM7",
            Slug = "foreign-clinic-pm7",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        });
        var patientId = Guid.NewGuid();
        db.Patients.Add(new Domain.Patients.Patient
        {
            Id = patientId,
            FirstName = "Foreign",
            LastName = "OrgPatient",
            IsActive = true,
        });
        db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = foreignClinic,
            PatientId = patientId,
            LocalPatientNumber = "F-PM7",
            Status = Domain.Patients.ClinicPatientStatus.Active,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return patientId;
    }

    private async Task<AppointmentResponse> CreateCheckedInAppointmentAsync(int daysAhead)
    {
        await AuthenticateAsync(PatientAEmail, PatientAPassword);
        var created = await CreatePatientAppointmentAsync("dev-clinic-a", _doctorAStaffId, daysAhead);
        await AuthenticateAsync(DoctorAEmail, DoctorAPassword);
        var confirm = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created.Id}/confirm",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        created = (await confirm.Content.ReadFromJsonAsync<AppointmentResponse>())!;
        var checkIn = await _client!.PostAsJsonAsync(
            $"/api/v1/staff/appointments/{created.Id}/check-in",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        checkIn.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await checkIn.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    private async Task<AppointmentResponse> CreatePatientAppointmentAsync(
        string clinicCode,
        Guid doctorId,
        int daysAhead)
    {
        var response = await _client!.PostAsJsonAsync("/api/v1/patients/me/appointments", new
        {
            clinicCode,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(daysAhead),
            durationMinutes = 30,
            reason = "PM-7",
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    private async Task<Guid> GetDoctorIdAsync(string clinicSlug)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        return await db.StaffMembers.Where(s => s.Role == AppRoles.Doctor)
            .Join(db.Clinics.Where(c => c.Slug == clinicSlug), s => s.ClinicId, c => c.Id, (s, _) => s.Id)
            .FirstAsync();
    }

    private async Task AuthenticateAsync(string email, string password)
    {
        _client!.DefaultRequestHeaders.Authorization = null;
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK, because: $"login must succeed for {email}");
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }

    private static async Task AssertPatientSafeAppointmentJsonAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                AssertAppointmentItem(item);
            }
        }
        else
        {
            AssertAppointmentItem(root);
        }

        await Task.CompletedTask;
    }

    private static void AssertAppointmentItem(JsonElement item)
    {
        if (item.TryGetProperty("patientDisplayName", out var display))
        {
            display.ValueKind.Should().Be(JsonValueKind.Null);
        }

        if (item.TryGetProperty("localPatientNumber", out var local))
        {
            local.ValueKind.Should().Be(JsonValueKind.Null);
        }

        item.TryGetProperty("noteBody", out _).Should().BeFalse();
        item.TryGetProperty("diagnosis", out _).Should().BeFalse();
        item.TryGetProperty("amendments", out _).Should().BeFalse();
        item.TryGetProperty("auditTrail", out _).Should().BeFalse();
    }

    private static string PasswordFor(string email) => email switch
    {
        PatientAEmail => PatientAPassword,
        PatientBEmail => PatientBPassword,
        UnlinkedEmail => UnlinkedPassword,
        InactiveEmail => InactivePassword,
        DoctorAEmail => DoctorAPassword,
        DoctorBEmail => DoctorBPassword,
        ClinicAdminEmail => ClinicAdminPassword,
        OrgAdminEmail => OrgAdminPassword,
        PlatformAdminEmail => PlatformAdminPassword,
        NurseEmail => NursePassword,
        ReceptionistEmail => ReceptionistPassword,
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
        HttpStatusCode Expected);
}
