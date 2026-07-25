using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Domain.Appointments;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminOperationsSmokeTests : E2ePageTestBase
{
    public ClinicAdminOperationsSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_Retry_Failed_Reminder_Without_Org_Or_Hangfire_Controls()
    {
        try
        {
            var seeded = await SeedFailedReminderAsync();

            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/operations/health");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Operations Health" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Clinic-scoped", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-testid='operations-health-clinic-caption']")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Hangfire queues", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Hangfire" })).ToHaveCountAsync(0);

            await Page.GotoAsync("/operations/reminders");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Reminders" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("your clinic", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-testid='operations-reminders-clinic-caption']")).ToBeVisibleAsync();
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Hangfire", new() { Exact = false })).ToHaveCountAsync(0);

            await Page.GetByRole(AriaRole.Button, new() { Name = "Search reminders" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Reminder list" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            var row = Page.Locator("table.hc-table tbody tr")
                .Filter(new() { HasText = seeded.AppointmentId.ToString("D")[..8] });
            await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(row.GetByText("Failed", new() { Exact = true })).ToBeVisibleAsync();

            await row.GetByRole(AriaRole.Button, new() { Name = "Retry reminder" }).ClickAsync();
            var confirm = Page.Locator(".ant-modal").Filter(new() { HasText = "Retry reminder" });
            await Expect(confirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await confirm.GetByRole(AriaRole.Button, new() { Name = "OK" }).ClickAsync();

            await Expect(Page.GetByText("Reminder queued for retry.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.ReloadAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Reminders" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Page.Locator("#reminder-status").ClickAsync();
            await Page.GetByRole(AriaRole.Option, new() { Name = "Pending", Exact = true }).ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Search reminders" }).ClickAsync();

            row = Page.Locator("table.hc-table tbody tr")
                .Filter(new() { HasText = seeded.AppointmentId.ToString("D")[..8] });
            await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(row.GetByText("Pending", new() { Exact = true })).ToBeVisibleAsync();

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Hangfire", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_Retry_Failed_Reminder_Without_Org_Or_Hangfire_Controls));
            throw;
        }
    }

    private async Task<(Guid AppointmentId, Guid ReminderId)> SeedFailedReminderAsync()
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

        var slotDay = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(5);
        var appointmentDateUtc = new DateTimeOffset(
                slotDay.ToDateTime(new TimeOnly(11, 0), DateTimeKind.Unspecified),
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
            });
        create.EnsureSuccessStatusCode();
        var appointment = await create.Content.ReadFromJsonAsync<AppointmentResponse>();

        await using var db = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>()
                .UseNpgsql(Host.ConnectionString)
                .Options);
        var reminder = await db.AppointmentReminders.FirstAsync(r => r.AppointmentId == appointment!.Id);
        reminder.Status = AppointmentReminderStatus.Failed;
        reminder.LastError = "simulated_delivery_failure";
        reminder.AttemptCount = 1;
        await db.SaveChangesAsync();

        return (appointment!.Id, reminder.Id);
    }
}
