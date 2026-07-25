using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminAppointmentsSmokeTests : E2ePageTestBase
{
    public ClinicAdminAppointmentsSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_Mark_No_Show_Without_Org_Controls()
    {
        try
        {
            var created = await CreateDedicatedClinicAppointmentAsync();

            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/appointments");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Appointment Queue" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Your clinic", new() { Exact = false })).ToBeVisibleAsync();

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.Locator("[data-testid='appointments-clinic-caption']")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Usage & Limits" })).ToHaveCountAsync(0);

            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Clinic appointments" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.GetByRole(AriaRole.Button, new() { Name = $"Details for appointment {created.Id}" })
                .ClickAsync();
            var dialog = Page.Locator(".ant-modal").Filter(new() { HasText = "Confirmed" });
            await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(dialog.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" })).ToHaveCountAsync(0);

            await dialog.GetByRole(AriaRole.Button, new() { Name = "No-show" }).ClickAsync();
            var confirm = Page.Locator(".ant-modal").Filter(new() { HasText = "Confirm no-show" });
            await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await confirm.GetByRole(AriaRole.Button, new() { Name = "OK" }).ClickAsync();

            await Expect(Page.GetByText("No-show succeeded.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(dialog.GetByText("NoShow")).ToBeVisibleAsync(new() { Timeout = 20_000 });

            await Page.ReloadAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Appointment Queue" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            // Terminal appointments are excluded from default queue — filter to NoShow.
            await Page.Locator("#queue-status").ClickAsync();
            await Page.GetByRole(AriaRole.Option, new() { Name = "NoShow", Exact = true }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Search appointment queue" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Details for appointment {created.Id}" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_Mark_No_Show_Without_Org_Controls));
            throw;
        }
    }

    private async Task<AppointmentResponse> CreateDedicatedClinicAppointmentAsync()
    {
        using var api = new HttpClient { BaseAddress = new Uri(Host.ApiBaseUrl.TrimEnd('/') + "/") };
        var login = await api.PostAsJsonAsync(
            "api/v1/auth/login",
            new LoginRequest
            {
                Email = Host.Users.ClinicAdminEmail,
                Password = Host.Users.ClinicAdminPassword,
            });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        api.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var me = await api.GetFromJsonAsync<CurrentUserResponse>("api/v1/auth/me");
        var clinicId = me!.ClinicId!.Value;

        var doctors = await api.GetFromJsonAsync<IReadOnlyList<ClinicDoctorResponse>>(
            $"api/v1/staff/clinics/{clinicId:D}/doctors");
        var doctorId = doctors!.First().StaffMemberId;

        var patients = await api.GetFromJsonAsync<PagedResponse<HealthCare.Contracts.Patients.StaffPatientLookupItemResponse>>(
            $"api/v1/staff/patients/lookup?clinicId={clinicId:D}&pageSize=5");
        var patientId = patients!.Items.First().PatientId;

        var slotDay = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(3);
        var appointmentDateUtc = new DateTimeOffset(
                slotDay.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Unspecified),
                TimeSpan.FromHours(3))
            .ToUniversalTime();

        var create = await api.PostAsJsonAsync(
            "api/v1/staff/appointments",
            new CreateStaffAppointmentRequest
            {
                PatientId = patientId,
                DoctorStaffMemberId = doctorId,
                AppointmentDateUtc = appointmentDateUtc,
                DurationMinutes = 30,
                Reason = $"CA6-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<AppointmentResponse>();
        created.Should().NotBeNull();
        created!.Status.Should().Be("Confirmed");
        return created;
    }
}
