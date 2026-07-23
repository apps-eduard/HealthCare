using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

public sealed class MedicalNotesEndpointTests : IAsyncLifetime
{
    private const string PatientEmail = "patient@healthcare.local";
    private const string PatientPassword = "ChangeMe_Patient_1!";
    private const string DoctorAEmail = "doctor.a@healthcare.local";
    private const string DoctorAPassword = "ChangeMe_DoctorA_1!";
    private const string DoctorBEmail = "doctor.b@healthcare.local";
    private const string DoctorBPassword = "ChangeMe_DoctorB_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_medical_notes_test")
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
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Jwt:Issuer", "HealthCare");
            builder.UseSetting("Jwt:Audience", "HealthCare");
            builder.UseSetting("Jwt:SigningKey", "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+");
            builder.UseSetting("DevelopmentSeed:Admin:Email", "admin@healthcare.local");
            builder.UseSetting("DevelopmentSeed:Admin:Password", "ChangeMe_Admin_1!");
            builder.UseSetting("DevelopmentSeed:Patient:Email", PatientEmail);
            builder.UseSetting("DevelopmentSeed:Patient:Password", PatientPassword);
            builder.UseSetting("DevelopmentSeed:Patient:StaffEmail", DoctorAEmail);
            builder.UseSetting("DevelopmentSeed:Patient:StaffPassword", DoctorAPassword);
            builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffEmail", DoctorBEmail);
            builder.UseSetting("DevelopmentSeed:Patient:OtherClinicStaffPassword", DoctorBPassword);
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
        if (_factory is not null) await _factory.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Anonymous_Create_Returns_401()
    {
        var response = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{Guid.NewGuid()}/medical-notes", CreateDraft());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_And_Receptionist_Cannot_Create_Medical_Notes()
    {
        var appointment = await CreateEligibleAppointmentAsync();

        await AuthenticateAsync(PatientEmail, PatientPassword);
        var patient = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{appointment.Id}/medical-notes", CreateDraft());
        patient.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        const string receptionistEmail = "notes.receptionist@healthcare.local";
        const string receptionistPassword = "ChangeMe_Receptionist_1!";
        await SeedStaffUserAsync(receptionistEmail, receptionistPassword, AppRoles.Receptionist, "dev-clinic-a");
        await AuthenticateAsync(receptionistEmail, receptionistPassword);
        var receptionist = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{appointment.Id}/medical-notes", CreateDraft());
        receptionist.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Doctor_Creates_Updates_Signs_And_Amends_Note()
    {
        var appointment = await CreateEligibleAppointmentAsync();
        var create = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{appointment.Id}/medical-notes", CreateDraft());
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var draft = (await create.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!;

        var stale = await _client!.PatchAsJsonAsync($"/api/v1/medical-notes/{draft.Id}/draft",
            new UpdateMedicalNoteDraftRequest { ExpectedVersion = draft.Version + 1, Plan = "Stale" });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var update = await _client!.PatchAsJsonAsync($"/api/v1/medical-notes/{draft.Id}/draft",
            new UpdateMedicalNoteDraftRequest { ExpectedVersion = draft.Version, Plan = "Updated plan" });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!;

        var sign = await _client!.PostAsJsonAsync($"/api/v1/medical-notes/{draft.Id}/sign",
            new SignMedicalNoteRequest { ExpectedVersion = updated.Version });
        sign.StatusCode.Should().Be(HttpStatusCode.OK);
        var signed = (await sign.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!;

        var signedUpdate = await _client!.PatchAsJsonAsync($"/api/v1/medical-notes/{draft.Id}/draft",
            new UpdateMedicalNoteDraftRequest { ExpectedVersion = signed.Version, Plan = "Forbidden" });
        signedUpdate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var amend = await _client!.PostAsJsonAsync($"/api/v1/medical-notes/{draft.Id}/amend",
            new AmendMedicalNoteRequest
            {
                ExpectedVersion = signed.Version,
                AmendmentReason = "Correction",
                Plan = "Corrected plan",
            });
        amend.StatusCode.Should().Be(HttpStatusCode.OK);
        var amendment = (await amend.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!;

        amendment.Status.Should().Be("Signed");
        amendment.AmendsMedicalNoteId.Should().Be(draft.Id);
        var original = (await _client!.GetFromJsonAsync<MedicalNoteDetailResponse>($"/api/v1/medical-notes/{draft.Id}"))!;
        original.Plan.Should().Be("Updated plan");
    }

    [Fact]
    public async Task Cross_Clinic_Doctor_Cannot_Read_Note_Content()
    {
        var appointment = await CreateEligibleAppointmentAsync();
        var create = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{appointment.Id}/medical-notes", CreateDraft());
        var note = (await create.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!;

        await AuthenticateAsync(DoctorBEmail, DoctorBPassword);
        var response = await _client!.GetAsync($"/api/v1/medical-notes/{note.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(AppRoles.ClinicAdmin)]
    [InlineData(AppRoles.OrganizationAdmin)]
    [InlineData(AppRoles.PlatformAdmin)]
    public async Task Administrative_Roles_Cannot_Read_Note_Content(string role)
    {
        var appointment = await CreateEligibleAppointmentAsync();
        var create = await _client!.PostAsJsonAsync(
            $"/api/v1/appointments/{appointment.Id}/medical-notes", CreateDraft());
        var note = (await create.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>())!;

        var email = $"notes.{role.ToLowerInvariant()}@healthcare.local";
        const string password = "ChangeMe_AdminRole_1!";
        await SeedStaffUserAsync(email, password, role, "dev-clinic-a");
        await AuthenticateAsync(email, password);
        var response = await _client!.GetAsync($"/api/v1/medical-notes/{note.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<AppointmentResponse> CreateEligibleAppointmentAsync()
    {
        await AuthenticateAsync(DoctorAEmail, DoctorAPassword);
        var patientId = await GetSeedPatientIdAsync();
        var doctorId = await GetDoctorIdAsync("dev-clinic-a");
        var create = await _client!.PostAsJsonAsync("/api/v1/staff/appointments", new
        {
            patientId,
            doctorStaffMemberId = doctorId,
            appointmentDateUtc = AlignedFutureSlotUtc(30),
            durationMinutes = 30,
        });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var appointment = (await create.Content.ReadFromJsonAsync<AppointmentResponse>())!;
        var checkIn = await _client!.PostAsJsonAsync($"/api/v1/staff/appointments/{appointment.Id}/check-in",
            new AppointmentActionRequest { ExpectedVersion = appointment.Version });
        checkIn.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await checkIn.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    private async Task SeedStaffUserAsync(string email, string password, string role, string clinicSlug)
    {
        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
        var clinic = await db.Clinics.SingleAsync(c => c.Slug == clinicSlug);
        var roleEntity = await db.Roles.SingleAsync(r => r.Name == role);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            IsActive = true,
        };
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, password);
        db.Users.Add(user);
        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = roleEntity.Id });
        db.StaffMembers.Add(new StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrganizationId = clinic.OrganizationId,
            ClinicId = clinic.Id,
            Role = role,
            FirstName = "Test",
            LastName = role,
            IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> GetSeedPatientIdAsync()
    {
        using var scope = _factory!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HealthCareDbContext>()
            .Patients.Select(p => p.Id).FirstAsync();
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

    private static CreateMedicalNoteDraftRequest CreateDraft() => new()
    {
        NoteType = "Progress",
        Subjective = "Clinical history",
        Assessment = "Assessment",
        Plan = "Care plan",
    };

    private static DateTimeOffset AlignedFutureSlotUtc(int daysAhead)
    {
        var localDate = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(daysAhead);
        return new DateTimeOffset(localDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Unspecified), TimeSpan.FromHours(3))
            .ToUniversalTime();
    }
}
