using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HealthCare.EndToEndTests;

/// <summary>
/// PM-8 helpers: Patient Mobile MVP journeys against the real API host (mobile client contract).
/// Does not drive the Android MAUI UI — see PatientAndroidRuntimeChecklist.md.
/// </summary>
internal static class PatientE2eApi
{
    private static int _slotSequence;
    private static int _emailSequence;

    public static string NextPatientEmail(string prefix = "pm8.patient") =>
        $"{prefix}.{Interlocked.Increment(ref _emailSequence)}.{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}@healthcare.local";

    public static async Task<HttpClient> AuthenticateAsync(E2eHostFixture host, string email, string password)
    {
        var api = CreateAnonymousClient(host);
        var login = await api.PostAsJsonAsync(
            "api/v1/auth/login",
            new LoginRequest { Email = email, Password = password });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        tokens.Should().NotBeNull();
        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        api.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return api;
    }

    public static HttpClient CreateAnonymousClient(E2eHostFixture host) =>
        new() { BaseAddress = new Uri(host.ApiBaseUrl.TrimEnd('/') + "/") };

    public static async Task<(string Email, string Password)> RegisterAndConfirmPatientAsync(
        E2eHostFixture host,
        string? email = null,
        string password = "ChangeMe_Pm8Patient_1!")
    {
        email ??= NextPatientEmail();
        using var anon = CreateAnonymousClient(host);
        var register = await anon.PostAsJsonAsync(
            "api/v1/auth/register/patient",
            new PatientRegisterRequest
            {
                Email = email,
                Password = password,
                ConfirmPassword = password,
                FirstName = "Pm8",
                LastName = "Patient",
            });
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBefore = await anon.PostAsJsonAsync(
            "api/v1/auth/login",
            new LoginRequest { Email = email, Password = password });
        loginBefore.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var tokenResponse = await anon.GetAsync(
            $"api/v1/auth/dev/confirmation-token?email={Uri.EscapeDataString(email)}");
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var token = tokenDoc.RootElement.GetProperty("token").GetString();
        token.Should().NotBeNullOrWhiteSpace();

        var confirm = await anon.PostAsJsonAsync(
            "api/v1/auth/confirm-email",
            new ConfirmEmailRequest { Email = email, Token = token! });
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        return (email, password);
    }

    public static async Task<(string Email, string Password)> SeedUnlinkedPatientUserAsync(
        E2eHostFixture host,
        string? email = null,
        string password = "ChangeMe_Pm8Unlinked_1!")
    {
        email ??= NextPatientEmail("pm8.unlinked");
        await using var services = CreateIdentityServices(host);
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
        };
        var create = await users.CreateAsync(user, password);
        create.Succeeded.Should().BeTrue(string.Join("; ", create.Errors.Select(e => e.Description)));
        var role = await users.AddToRoleAsync(user, AppRoles.Patient);
        role.Succeeded.Should().BeTrue(string.Join("; ", role.Errors.Select(e => e.Description)));
        return (email, password);
    }

    public static async Task<(string Email, string Password, Guid PatientId)> SeedSecondLinkedPatientAsync(
        E2eHostFixture host,
        string? email = null,
        string password = "ChangeMe_Pm8PatientB_1!")
    {
        // Prefer the public registration + confirm path (same as the mobile app) over Identity DI shims.
        email ??= NextPatientEmail("pm8.b");
        var (createdEmail, createdPassword) = await RegisterAndConfirmPatientAsync(host, email, password);
        using var api = await AuthenticateAsync(host, createdEmail, createdPassword);
        await EnsureEnrolledAsync(api);
        var me = await api.GetFromJsonAsync<CurrentUserResponse>("api/v1/auth/me");
        me!.PatientId.Should().NotBeNull();
        return (createdEmail, createdPassword, me.PatientId!.Value);
    }

    public static async Task EnsureEnrolledAsync(HttpClient api, string clinicCode = "dev-clinic-a")
    {
        var enroll = await api.PostAsJsonAsync(
            "api/v1/patients/me/clinics/register",
            new RegisterPatientWithClinicRequest { ClinicCode = clinicCode });
        enroll.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Conflict);
    }

    public static async Task<Guid> GetClinicADoctorStaffIdAsync(E2eHostFixture host)
    {
        await using var db = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>().UseNpgsql(host.ConnectionString).Options);
        return await db.StaffMembers.Where(s => s.Role == AppRoles.Doctor)
            .Join(db.Clinics.Where(c => c.Slug == "dev-clinic-a"), s => s.ClinicId, c => c.Id, (s, _) => s.Id)
            .FirstAsync();
    }

    public static async Task<AvailableSlotResponse> PickAvailableSlotAsync(
        HttpClient api,
        string clinicCode,
        Guid doctorStaffId)
    {
        for (var day = 10; day <= 45; day++)
        {
            var localDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(day));
            if (localDate.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
            {
                continue;
            }

            var slots = await api.GetFromJsonAsync<IReadOnlyList<AvailableSlotResponse>>(
                $"api/v1/clinics/{clinicCode}/doctors/{doctorStaffId:D}/available-slots?date={localDate:yyyy-MM-dd}");
            var free = slots?.FirstOrDefault();
            if (free is not null)
            {
                return free;
            }
        }

        throw new InvalidOperationException("No available future slot found for PM-8 seed.");
    }

    public static async Task<AppointmentResponse> BookRequestedAsync(
        HttpClient api,
        string clinicCode,
        Guid doctorStaffId,
        string? reason = null)
    {
        var slot = await PickAvailableSlotAsync(api, clinicCode, doctorStaffId);
        var create = await api.PostAsJsonAsync(
            "api/v1/patients/me/appointments",
            new CreatePatientAppointmentRequest
            {
                ClinicCode = clinicCode,
                DoctorStaffMemberId = doctorStaffId,
                AppointmentDateUtc = slot.StartUtc,
                DurationMinutes = Math.Max(1, (int)(slot.EndUtc - slot.StartUtc).TotalMinutes),
                Reason = reason ?? $"PM8-{Interlocked.Increment(ref _slotSequence)}",
            });
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();
        created.Should().NotBeNull();
        created!.Status.Should().Be("Requested");
        return created;
    }

    public static async Task SetAppointmentStartAsync(E2eHostFixture host, Guid appointmentId, DateTimeOffset startUtc)
    {
        await using var db = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>().UseNpgsql(host.ConnectionString).Options);
        var appt = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        appt.AppointmentDateUtc = startUtc;
        await db.SaveChangesAsync();
    }

    public static async Task<AppointmentResponse> ConfirmAsDoctorAsync(
        E2eHostFixture host,
        AppointmentResponse appointment)
    {
        using var doctor = await AuthenticateAsync(host, host.Users.DoctorEmail, host.Users.DoctorPassword);
        var confirm = await doctor.PostAsJsonAsync(
            $"api/v1/staff/appointments/{appointment.Id}/confirm",
            new AppointmentActionRequest { ExpectedVersion = appointment.Version });
        confirm.EnsureSuccessStatusCode();
        return (await confirm.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    public static async Task AssertPatientSafeAppointmentJsonAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        void CheckItem(JsonElement item)
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
        }

        var root = doc.RootElement;
        if (root.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                CheckItem(item);
            }
        }
        else
        {
            CheckItem(root);
        }

        await Task.CompletedTask;
    }

    private static ServiceProvider CreateIdentityServices(E2eHostFixture host)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDataProtection();
        services.AddDbContext<HealthCareDbContext>(o => o.UseNpgsql(host.ConnectionString));
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>()
            .AddDefaultTokenProviders();
        return services.BuildServiceProvider();
    }
}
