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
public sealed class ClinicAdminReportsSmokeTests : E2ePageTestBase
{
    public ClinicAdminReportsSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_View_Clinic_Reports_Without_Picker_Or_Export()
    {
        try
        {
            await SeedDeterministicReportDataAsync();

            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/clinic/reports");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Reports" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.Locator("[data-testid='clinic-reports-clinic-caption']")).ToBeVisibleAsync();
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export CSV" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export PDF" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Schedule report", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);

            await Expect(Page.GetByText("Total appointments", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Appointments by status" }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Appointments by status" }))
                .ToContainTextAsync("Completed");
            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Appointment volume by date" }))
                .ToBeVisibleAsync();

            await Page.GetByRole(AriaRole.Tab, new() { Name = "Doctors" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Appointments by doctor" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.GetByRole(AriaRole.Tab, new() { Name = "Operations" }).ClickAsync();
            await Expect(Page.GetByText("Failed reminders", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.GetByText("Failed summary runs", new() { Exact = false }))
                .ToBeVisibleAsync();

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export CSV" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_View_Clinic_Reports_Without_Picker_Or_Export));
            throw;
        }
    }

    private async Task SeedDeterministicReportDataAsync()
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

        var slotDay = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(2);
        var appointmentDateUtc = new DateTimeOffset(
                slotDay.ToDateTime(new TimeOnly(10, 30), DateTimeKind.Unspecified),
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
        appointment.Should().NotBeNull();

        await using var db = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>()
                .UseNpgsql(Host.ConnectionString)
                .Options);

        var appt = await db.Appointments.SingleAsync(a => a.Id == appointment!.Id);
        appt.Status = AppointmentStatus.Completed;
        // Keep the appointment inside the default clinic-report window (last ~30 days).
        appt.AppointmentDateUtc = DateTimeOffset.UtcNow.AddDays(-1);

        var reminder = await db.AppointmentReminders.FirstAsync(r => r.AppointmentId == appointment.Id);
        reminder.Status = AppointmentReminderStatus.Failed;
        reminder.ScheduledAtUtc = DateTimeOffset.UtcNow.AddHours(-2);
        reminder.AttemptCount = 1;
        reminder.LastError = "ca8_seed_failure";

        var orgId = appt.OrganizationId;
        var summaryDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (!await db.ClinicAppointmentSummaryRuns.AnyAsync(r =>
                r.ClinicId == clinicId && r.SummaryDate == summaryDate && r.Status == ClinicAppointmentSummaryRunStatus.Failed))
        {
            db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                OrganizationId = orgId,
                SummaryDate = summaryDate,
                ScheduledAtUtc = DateTimeOffset.UtcNow,
                Status = ClinicAppointmentSummaryRunStatus.Failed,
                AttemptCount = 1,
                IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicId, summaryDate) + ":ca8e2e",
                AppointmentCount = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }
}
