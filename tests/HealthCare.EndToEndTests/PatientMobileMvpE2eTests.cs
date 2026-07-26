using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Infrastructure.Appointments;

namespace HealthCare.EndToEndTests;

/// <summary>
/// PM-8 Patient Mobile MVP end-to-end pack (Layer A): real API host journeys matching the MAUI client contract.
/// Android UI automation is not in-repo; runtime acceptance is documented in PatientAndroidRuntimeChecklist.md.
/// </summary>
[Collection(E2eCollection.Name)]
public sealed class PatientMobileMvpE2eTests
{
    private readonly E2eHostFixture _host;

    public PatientMobileMvpE2eTests(E2eHostFixture host) => _host = host;

    [Fact]
    public async Task Scenario01_Anonymous_Protected_Surfaces_Return_401()
    {
        using var anon = PatientE2eApi.CreateAnonymousClient(_host);
        (await anon.GetAsync("api/v1/patients/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("api/v1/patients/me/clinics")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("api/v1/patients/me/appointments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("api/v1/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("api/v1/staff/patients")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("api/v1/clinic/reports/appointments?fromDate=2026-01-01&toDate=2026-01-07"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Scenario02_03_Registration_Confirmation_And_Login_Linkage()
    {
        var (email, password) = await PatientE2eApi.RegisterAndConfirmPatientAsync(_host);
        using var api = await PatientE2eApi.AuthenticateAsync(_host, email, password);
        var me = await api.GetFromJsonAsync<CurrentUserResponse>("api/v1/auth/me");
        me!.HasLinkedPatient.Should().BeTrue();
        me.PatientId.Should().NotBeNull();
        me.Roles.Should().Contain("PATIENT");
        me.Permissions.Should().NotBeEmpty();

        var profile = await api.GetFromJsonAsync<PatientProfileResponse>("api/v1/patients/me");
        profile!.FirstName.Should().Be("Pm8");
        profile.LastName.Should().Be("Patient");
    }

    [Fact]
    public async Task Scenario03b_Unlinked_Patient_Cannot_Use_Self_Service()
    {
        var (email, password) = await PatientE2eApi.SeedUnlinkedPatientUserAsync(_host);
        using var api = await PatientE2eApi.AuthenticateAsync(_host, email, password);
        var me = await api.GetFromJsonAsync<CurrentUserResponse>("api/v1/auth/me");
        me!.HasLinkedPatient.Should().BeFalse();
        (await api.GetAsync("api/v1/patients/me")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.GetAsync("api/v1/patients/me/appointments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Scenario05_Profile_View_And_Edit_Persists()
    {
        using var api = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        var before = await api.GetFromJsonAsync<PatientProfileResponse>("api/v1/patients/me");
        before.Should().NotBeNull();

        var address = $"PM8-addr-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var patch = await api.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "api/v1/patients/me")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = before!.Version,
                address,
            }),
        });
        patch.StatusCode.Should().Be(HttpStatusCode.OK, await patch.Content.ReadAsStringAsync());
        var after = await patch.Content.ReadFromJsonAsync<PatientProfileResponse>();
        after!.Address.Should().Be(address);

        var reload = await api.GetFromJsonAsync<PatientProfileResponse>("api/v1/patients/me");
        reload!.Address.Should().Be(address);
        var raw = await (await api.GetAsync("api/v1/patients/me")).Content.ReadAsStringAsync();
        raw.ToLowerInvariant().Should().NotContain("medicalnote");
        raw.Should().NotContain("LocalPatientNumber");
    }

    [Fact]
    public async Task Scenario06_07_08_Clinic_Doctor_And_Availability_Discovery()
    {
        using var api = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);

        var clinics = await api.GetFromJsonAsync<PagedResponse<PatientClinicListItemResponse>>(
            "api/v1/patients/me/clinics?search=Dev&pageSize=20");
        clinics!.Items.Should().NotBeEmpty();
        clinics.Items.Should().Contain(c => c.ClinicCode == "dev-clinic-a");
        var clinicsJson = await (await api.GetAsync("api/v1/patients/me/clinics")).Content.ReadAsStringAsync();
        clinicsJson.ToLowerInvariant().Should().NotContain("organizationid");

        var detail = await api.GetFromJsonAsync<PatientClinicDetailResponse>("api/v1/patients/me/clinics/dev-clinic-a");
        detail!.Name.Should().NotBeNullOrWhiteSpace();
        detail.ClinicCode.Should().Be("dev-clinic-a");

        await PatientE2eApi.EnsureEnrolledAsync(api);
        detail = await api.GetFromJsonAsync<PatientClinicDetailResponse>("api/v1/patients/me/clinics/dev-clinic-a");
        detail!.IsEnrolled.Should().BeTrue();

        var invalid = await api.PostAsJsonAsync(
            "api/v1/patients/me/clinics/register",
            new RegisterPatientWithClinicRequest { ClinicCode = "no-such-clinic-pm8" });
        invalid.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);

        var doctors = await api.GetFromJsonAsync<IReadOnlyList<ClinicDoctorResponse>>(
            "api/v1/clinics/dev-clinic-a/doctors");
        doctors.Should().NotBeEmpty();
        var doctor = doctors![0];
        doctor.DisplayName.Should().NotBeNullOrWhiteSpace();
        var doctorsJson = (await (await api.GetAsync("api/v1/clinics/dev-clinic-a/doctors")).Content.ReadAsStringAsync())
            .ToLowerInvariant();
        doctorsJson.Should().NotContain("\"email\"");
        doctorsJson.Should().NotContain("userid");

        var slot = await PatientE2eApi.PickAvailableSlotAsync(api, "dev-clinic-a", doctor.StaffMemberId);
        slot.StartUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Scenario09_10_11_12_Book_List_Cancel_And_Reschedule()
    {
        using var api = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        await PatientE2eApi.EnsureEnrolledAsync(api);
        var doctorId = await PatientE2eApi.GetClinicADoctorStaffIdAsync(_host);

        var booked = await PatientE2eApi.BookRequestedAsync(api, "dev-clinic-a", doctorId, "PM8-book");
        booked.Status.Should().Be("Requested");
        booked.PatientDisplayName.Should().BeNull();
        booked.LocalPatientNumber.Should().BeNull();

        // Duplicate submit with same slot should conflict (no silent duplicate).
        var dup = await api.PostAsJsonAsync(
            "api/v1/patients/me/appointments",
            new CreatePatientAppointmentRequest
            {
                ClinicCode = "dev-clinic-a",
                DoctorStaffMemberId = doctorId,
                AppointmentDateUtc = booked.AppointmentDateUtc,
                DurationMinutes = booked.DurationMinutes,
                Reason = "PM8-dup",
            });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var listResponse = await api.GetAsync("api/v1/patients/me/appointments?pageSize=50");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await PatientE2eApi.AssertPatientSafeAppointmentJsonAsync(await listResponse.Content.ReadAsStringAsync());
        var list = await listResponse.Content.ReadFromJsonAsync<PagedResponse<AppointmentResponse>>();
        list!.Items.Should().Contain(a => a.Id == booked.Id);

        var detailResponse = await api.GetAsync($"api/v1/appointments/{booked.Id}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await PatientE2eApi.AssertPatientSafeAppointmentJsonAsync(await detailResponse.Content.ReadAsStringAsync());

        // Reschedule to a new slot (same identity).
        var newSlot = await PatientE2eApi.PickAvailableSlotAsync(api, "dev-clinic-a", doctorId);
        var reschedule = await api.PostAsJsonAsync(
            $"api/v1/appointments/{booked.Id}/reschedule",
            new RescheduleAppointmentRequest
            {
                DoctorStaffMemberId = doctorId,
                AppointmentDateUtc = newSlot.StartUtc,
                DurationMinutes = Math.Max(1, (int)(newSlot.EndUtc - newSlot.StartUtc).TotalMinutes),
                ExpectedVersion = booked.Version,
                Reason = booked.Reason,
            });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await reschedule.Content.ReadFromJsonAsync<AppointmentResponse>();
        moved!.Id.Should().Be(booked.Id);
        moved.AppointmentDateUtc.Should().Be(newSlot.StartUtc);
        moved.Status.Should().Be("Requested");

        // Cancel outside cutoff.
        var cancel = await api.PostAsJsonAsync(
            $"api/v1/appointments/{moved.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = moved.Version });
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await cancel.Content.ReadFromJsonAsync<AppointmentResponse>();
        cancelled!.Status.Should().Be("CancelledByPatient");
        cancelled.Id.Should().Be(booked.Id);
    }

    [Fact]
    public async Task Scenario11b_Cancel_Inside_Two_Hour_Cutoff_Returns_409()
    {
        using var api = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        await PatientE2eApi.EnsureEnrolledAsync(api);
        var doctorId = await PatientE2eApi.GetClinicADoctorStaffIdAsync(_host);
        var booked = await PatientE2eApi.BookRequestedAsync(api, "dev-clinic-a", doctorId, "PM8-cutoff");
        await PatientE2eApi.SetAppointmentStartAsync(
            _host,
            booked.Id,
            DateTimeOffset.UtcNow.Add(AppointmentService.PatientScheduleMutationCutoff).AddMinutes(-15));

        var cancel = await api.PostAsJsonAsync(
            $"api/v1/appointments/{booked.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = booked.Version });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errorCode").GetString()
            .Should().Be(AppointmentErrorCodes.PatientMutationCutoff);
    }

    [Fact]
    public async Task Scenario12b_Reschedule_Confirmed_Preserves_Identity()
    {
        using var api = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        await PatientE2eApi.EnsureEnrolledAsync(api);
        var doctorId = await PatientE2eApi.GetClinicADoctorStaffIdAsync(_host);
        var booked = await PatientE2eApi.BookRequestedAsync(api, "dev-clinic-a", doctorId, "PM8-confirmed");
        var confirmed = await PatientE2eApi.ConfirmAsDoctorAsync(_host, booked);

        using var patient = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        var newSlot = await PatientE2eApi.PickAvailableSlotAsync(patient, "dev-clinic-a", doctorId);
        var reschedule = await patient.PostAsJsonAsync(
            $"api/v1/appointments/{confirmed.Id}/reschedule",
            new RescheduleAppointmentRequest
            {
                DoctorStaffMemberId = doctorId,
                AppointmentDateUtc = newSlot.StartUtc,
                DurationMinutes = 30,
                ExpectedVersion = confirmed.Version,
            });
        reschedule.StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await reschedule.Content.ReadFromJsonAsync<AppointmentResponse>();
        moved!.Id.Should().Be(confirmed.Id);
        moved.Status.Should().BeOneOf("Requested", "Confirmed");
    }

    [Fact]
    public async Task Scenario13_15_Terminal_And_Restricted_Surfaces()
    {
        using var api = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        await PatientE2eApi.EnsureEnrolledAsync(api);
        var doctorId = await PatientE2eApi.GetClinicADoctorStaffIdAsync(_host);
        var booked = await PatientE2eApi.BookRequestedAsync(api, "dev-clinic-a", doctorId, "PM8-terminal");
        var cancelled = await api.PostAsJsonAsync(
            $"api/v1/appointments/{booked.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = booked.Version });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await cancelled.Content.ReadFromJsonAsync<AppointmentResponse>();

        // Terminal: cancel/reschedule denied with conflict/invalid transition — not staff success.
        var again = await api.PostAsJsonAsync(
            $"api/v1/appointments/{body!.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = body.Version });
        again.StatusCode.Should().BeOneOf(HttpStatusCode.Conflict, HttpStatusCode.BadRequest);

        (await api.GetAsync("api/v1/staff/patients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.PostAsJsonAsync(
            $"api/v1/staff/appointments/{body.Id}/confirm",
            new AppointmentActionRequest { ExpectedVersion = body.Version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.PostAsJsonAsync(
            $"api/v1/staff/appointments/{body.Id}/check-in",
            new AppointmentActionRequest { ExpectedVersion = body.Version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.PostAsJsonAsync(
            $"api/v1/staff/appointments/{body.Id}/complete",
            new AppointmentActionRequest { ExpectedVersion = body.Version })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.GetAsync("api/v1/clinic/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.GetAsync("api/v1/clinic/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.GetAsync("api/v1/doctor/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.GetAsync("api/v1/organization/dashboard")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Scenario14_Cross_Patient_Concealment_Returns_404()
    {
        using var patientA = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        await PatientE2eApi.EnsureEnrolledAsync(patientA);
        var doctorId = await PatientE2eApi.GetClinicADoctorStaffIdAsync(_host);
        var owned = await PatientE2eApi.BookRequestedAsync(patientA, "dev-clinic-a", doctorId, "PM8-own");

        var (emailB, passwordB, _) = await PatientE2eApi.SeedSecondLinkedPatientAsync(_host);
        using var patientB = await PatientE2eApi.AuthenticateAsync(_host, emailB, passwordB);
        await PatientE2eApi.EnsureEnrolledAsync(patientB);

        (await patientB.GetAsync($"api/v1/appointments/{owned.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cancel = await patientB.PostAsJsonAsync(
            $"api/v1/appointments/{owned.Id}/cancel",
            new AppointmentActionRequest { ExpectedVersion = 0 });
        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var cancelBody = await cancel.Content.ReadAsStringAsync();
        cancelBody.Should().NotContain("patient_mutation_cutoff");
        cancelBody.Should().NotContain("concurrency_conflict");
        cancelBody.Should().NotContain("slot_conflict");

        var reschedule = await patientB.PostAsJsonAsync(
            $"api/v1/appointments/{owned.Id}/reschedule",
            new RescheduleAppointmentRequest
            {
                AppointmentDateUtc = DateTimeOffset.UtcNow.AddDays(20),
                DurationMinutes = 30,
                ExpectedVersion = 0,
            });
        reschedule.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Scenario16_Logout_Revokes_Refresh_And_Blocks_Reuse()
    {
        using var anon = PatientE2eApi.CreateAnonymousClient(_host);
        var login = await anon.PostAsJsonAsync(
            "api/v1/auth/login",
            new LoginRequest
            {
                Email = _host.Users.PatientEmail,
                Password = _host.Users.PatientPassword,
            });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        tokens.Should().NotBeNull();

        anon.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        (await anon.GetAsync("api/v1/patients/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var logout = await anon.PostAsJsonAsync(
            "api/v1/auth/logout",
            new LogoutRequest { RefreshToken = tokens.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await PatientE2eApi.CreateAnonymousClient(_host).PostAsJsonAsync(
            "api/v1/auth/refresh",
            new RefreshTokenRequest { RefreshToken = tokens.RefreshToken });
        refresh.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Scenario04_Seeded_Patient_Session_Me_Is_Linked()
    {
        // Mobile session restoration validates /auth/me after secure-token restore (covered in Mobile.Tests).
        // Here we prove the seeded Patient identity the MAUI app uses remains linked and ready.
        using var api = await PatientE2eApi.AuthenticateAsync(
            _host, _host.Users.PatientEmail, _host.Users.PatientPassword);
        var me = await api.GetFromJsonAsync<CurrentUserResponse>("api/v1/auth/me");
        me!.HasLinkedPatient.Should().BeTrue();
        me.Email.Should().Be(_host.Users.PatientEmail);
    }
}
