using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.MedicalNotes;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.EndToEndTests;

/// <summary>
/// API helpers for Doctor DR-10 journeys. Seeds appointments for doctor.a / doctor.b without relying on browser create.
/// </summary>
internal static class DoctorE2eApi
{
    private static int _slotSequence;

    public static async Task<AppointmentResponse> CreateCheckedInOwnAppointmentAsync(
        E2eHostFixture host,
        string reasonPrefix)
    {
        var doctorStaffId = await GetDoctorStaffIdAsync(host, host.Users.DoctorEmail, host.Users.DoctorPassword);
        var created = await CreateStaffAppointmentForDoctorAsync(host, doctorStaffId, reasonPrefix);
        return await CheckInAsDoctorAsync(host, created, host.Users.DoctorEmail, host.Users.DoctorPassword);
    }

    public static async Task<AppointmentResponse> CreatePeerClinicBAppointmentAsync(
        E2eHostFixture host,
        string reasonPrefix)
    {
        using var api = await AuthenticateAsync(host, host.Users.PatientEmail, host.Users.PatientPassword);
        var doctorBId = await GetDoctorStaffIdForClinicSlugAsync(host, "dev-clinic-b");
        var appointmentDateUtc = NextUniqueSlotUtc();

        var create = await api.PostAsJsonAsync(
            "api/v1/patients/me/appointments",
            new CreatePatientAppointmentRequest
            {
                ClinicCode = "dev-clinic-b",
                DoctorStaffMemberId = doctorBId,
                AppointmentDateUtc = appointmentDateUtc,
                DurationMinutes = 30,
                Reason = $"{reasonPrefix}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();
        created.Should().NotBeNull();

        using var doctorB = await AuthenticateAsync(host, "doctor.b@healthcare.local", "ChangeMe_DoctorB_1!");
        var confirm = await doctorB.PostAsJsonAsync(
            $"api/v1/staff/appointments/{created!.Id}/confirm",
            new AppointmentActionRequest { ExpectedVersion = created.Version });
        confirm.EnsureSuccessStatusCode();
        return (await confirm.Content.ReadFromJsonAsync<AppointmentResponse>())!;
    }

    /// <summary>
    /// Peer clinic-B appointment with a draft note authored by doctor.b (for concealment assertions).
    /// </summary>
    public static async Task<(AppointmentResponse Appointment, Guid NoteId)> CreatePeerClinicBAppointmentWithNoteAsync(
        E2eHostFixture host,
        string reasonPrefix)
    {
        var confirmed = await CreatePeerClinicBAppointmentAsync(host, reasonPrefix);
        var checkedIn = await CheckInAsDoctorAsync(
            host,
            confirmed,
            "doctor.b@healthcare.local",
            "ChangeMe_DoctorB_1!");

        using var doctorB = await AuthenticateAsync(host, "doctor.b@healthcare.local", "ChangeMe_DoctorB_1!");
        var createNote = await doctorB.PostAsJsonAsync(
            $"api/v1/appointments/{checkedIn.Id}/medical-notes",
            new CreateMedicalNoteDraftRequest
            {
                NoteType = "Progress",
                Plan = $"PEER-SECRET-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            });
        createNote.EnsureSuccessStatusCode();
        var note = await createNote.Content.ReadFromJsonAsync<MedicalNoteDetailResponse>();
        note.Should().NotBeNull();
        return (checkedIn, note!.Id);
    }

    public static async Task<int> CountNotesForAppointmentAsync(E2eHostFixture host, Guid appointmentId)
    {
        await using var db = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>()
                .UseNpgsql(host.ConnectionString)
                .Options);
        return await db.MedicalNotes.CountAsync(n => n.AppointmentId == appointmentId);
    }

    public static async Task<HttpStatusCode> GetNoteStatusAsDoctorAsync(E2eHostFixture host, Guid noteId)
    {
        using var api = await AuthenticateAsync(host, host.Users.DoctorEmail, host.Users.DoctorPassword);
        var response = await api.GetAsync($"api/v1/medical-notes/{noteId:D}");
        return response.StatusCode;
    }

    private static async Task<AppointmentResponse> CreateStaffAppointmentForDoctorAsync(
        E2eHostFixture host,
        Guid doctorStaffId,
        string reasonPrefix)
    {
        using var api = await AuthenticateAsync(host, host.Users.ClinicAdminEmail, host.Users.ClinicAdminPassword);
        var me = await api.GetFromJsonAsync<CurrentUserResponse>("api/v1/auth/me");
        var clinicId = me!.ClinicId!.Value;

        var patients = await api.GetFromJsonAsync<PagedResponse<StaffPatientLookupItemResponse>>(
            $"api/v1/staff/patients/lookup?clinicId={clinicId:D}&pageSize=5");
        var patientId = patients!.Items.First().PatientId;

        var create = await api.PostAsJsonAsync(
            "api/v1/staff/appointments",
            new CreateStaffAppointmentRequest
            {
                PatientId = patientId,
                DoctorStaffMemberId = doctorStaffId,
                AppointmentDateUtc = NextUniqueSlotUtc(),
                DurationMinutes = 30,
                Reason = $"{reasonPrefix}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();
        created.Should().NotBeNull();
        created!.Status.Should().Be("Confirmed");
        created.DoctorStaffMemberId.Should().Be(doctorStaffId);
        return created;
    }

    private static async Task<AppointmentResponse> CheckInAsDoctorAsync(
        E2eHostFixture host,
        AppointmentResponse appointment,
        string email,
        string password)
    {
        using var api = await AuthenticateAsync(host, email, password);
        var checkIn = await api.PostAsJsonAsync(
            $"api/v1/staff/appointments/{appointment.Id}/check-in",
            new AppointmentActionRequest { ExpectedVersion = appointment.Version });
        checkIn.EnsureSuccessStatusCode();
        var checkedIn = await checkIn.Content.ReadFromJsonAsync<AppointmentResponse>();
        checkedIn.Should().NotBeNull();
        checkedIn!.Status.Should().Be("CheckedIn");
        return checkedIn;
    }

    private static DateTimeOffset NextUniqueSlotUtc()
    {
        var n = Interlocked.Increment(ref _slotSequence);
        // Must land inside the Appointment Queue default date window (typically today .. today+7).
        // Use afternoon slots to avoid collisions with other E2E suites (morning day+2..+5).
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var maxDay = today.AddDays(7);
        var slotDay = today.AddDays(1 + ((n - 1) % 6));
        while (slotDay.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday || slotDay > maxDay)
        {
            slotDay = slotDay.AddDays(1);
            if (slotDay > maxDay)
            {
                slotDay = today.AddDays(1);
            }

            if (slotDay.DayOfWeek is not DayOfWeek.Friday and not DayOfWeek.Saturday && slotDay <= maxDay)
            {
                break;
            }
        }

        // 30-minute slots (matches default availability) to avoid overlap 409 conflicts.
        var minuteOfDay = 14 * 60 + ((n - 1) % 12) * 30;
        var hour = minuteOfDay / 60;
        var minute = minuteOfDay % 60;
        return new DateTimeOffset(
                slotDay.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified),
                TimeSpan.FromHours(3))
            .ToUniversalTime();
    }

    private static async Task<Guid> GetDoctorStaffIdAsync(E2eHostFixture host, string email, string password)
    {
        using var api = await AuthenticateAsync(host, email, password);
        var me = await api.GetFromJsonAsync<CurrentUserResponse>("api/v1/auth/me");
        me!.StaffMemberId.Should().NotBeNull();
        return me.StaffMemberId!.Value;
    }

    private static async Task<Guid> GetDoctorStaffIdForClinicSlugAsync(E2eHostFixture host, string clinicSlug)
    {
        await using var db = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>()
                .UseNpgsql(host.ConnectionString)
                .Options);
        return await db.StaffMembers
            .Where(s => s.Role == AppRoles.Doctor)
            .Join(db.Clinics.Where(c => c.Slug == clinicSlug), s => s.ClinicId, c => c.Id, (s, _) => s.Id)
            .SingleAsync();
    }

    private static async Task<HttpClient> AuthenticateAsync(E2eHostFixture host, string email, string password)
    {
        var api = new HttpClient { BaseAddress = new Uri(host.ApiBaseUrl.TrimEnd('/') + "/") };
        var login = await api.PostAsJsonAsync(
            "api/v1/auth/login",
            new LoginRequest { Email = email, Password = password });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        api.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return api;
    }
}
