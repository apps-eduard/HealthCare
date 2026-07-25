using System.Text.RegularExpressions;
using FluentAssertions;
using HealthCare.Contracts.Appointments;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

/// <summary>
/// DR-10: Doctor medical-note draft → sign → amend and completion with note.
/// </summary>
[Collection(E2eCollection.Name)]
public sealed class DoctorClinicalWorkflowSmokeTests : E2ePageTestBase
{
    public DoctorClinicalWorkflowSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Doctor_Note_Lifecycle_And_Complete_Own_Appointment()
    {
        try
        {
            var appointment = await DoctorE2eApi.CreateCheckedInOwnAppointmentAsync(Host, "DR10-NOTE");
            var originalPlan = "DR10 initial plan";

            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await OpenOwnAppointmentDetailAsync(appointment);

            var dialog = DetailDialog(appointment.Id);
            await Expect(dialog.GetByText("CheckedIn")).ToBeVisibleAsync();
            await Expect(dialog.GetByText("Medical notes")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" })).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" })).ToBeVisibleAsync();

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Create medical note draft" })
                .Or(dialog.GetByRole(AriaRole.Button, new() { Name = "New draft note" }))
                .ClickAsync();
            await Expect(Page.GetByText("Draft note created.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            dialog = DetailDialog(appointment.Id);
            var planField = dialog.Locator("#medical-note-plan");
            if (await planField.CountAsync() == 0)
            {
                await dialog.GetByRole(AriaRole.Button, new() { Name = "Open medical note" }).ClickAsync();
            }

            await Expect(dialog.Locator("#medical-note-plan")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await dialog.Locator("#medical-note-plan").FillAsync(originalPlan);
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Save medical note draft" })
                .Or(dialog.GetByRole(AriaRole.Button, new() { Name = "Save draft" }))
                .ClickAsync();
            await Expect(Page.GetByText("Draft note saved.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            var signButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Sign medical note" })
                .Or(dialog.GetByRole(AriaRole.Button, new() { Name = "Sign" }));
            await Expect(signButton).ToBeEnabledAsync(new() { Timeout = 30_000 });
            await signButton.ClickAsync();
            var signConfirm = Page.Locator(".ant-modal").Filter(new() { HasText = "Sign note" });
            await Expect(signConfirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await signConfirm.GetByRole(AriaRole.Button, new() { Name = "OK" }).ClickAsync();
            await Expect(Page.GetByText("Note signed.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            dialog = DetailDialog(appointment.Id);
            await Expect(dialog.GetByText("Signed", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Save medical note draft" }))
                .ToHaveCountAsync(0);
            await Expect(dialog.Locator("#medical-note-plan")).ToHaveCountAsync(0);
            await Expect(dialog.GetByText($"P: {originalPlan}")).ToBeVisibleAsync();

            await dialog.Locator("#medical-note-amend-reason").FillAsync("DR10 correction");
            await dialog.Locator("#medical-note-amend-plan").FillAsync("DR10 amended plan");
            var amendButton = dialog.GetByRole(AriaRole.Button, new() { Name = "Amend medical note" })
                .Or(dialog.GetByRole(AriaRole.Button, new() { Name = "Amend" }));
            await Expect(amendButton).ToBeEnabledAsync(new() { Timeout = 30_000 });
            await amendButton.ClickAsync();
            await Expect(Page.GetByText("Amendment signed.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            dialog = DetailDialog(appointment.Id);
            await Expect(dialog.GetByRole(AriaRole.Table, new() { Name = "Medical notes" })).ToBeVisibleAsync();
            var noteRows = dialog.Locator("table[aria-label='Medical notes'] tbody tr");
            await Expect(noteRows).ToHaveCountAsync(2, new() { Timeout = 20_000 });

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" }).ClickAsync();
            var completeConfirm = Page.Locator(".ant-modal").Filter(new() { HasText = "Complete" });
            await Expect(completeConfirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(completeConfirm.GetByText("medical note is not required", new() { Exact = false }))
                .ToBeVisibleAsync();
            await completeConfirm.GetByRole(AriaRole.Button, new() { Name = "OK" }).ClickAsync();
            await Expect(Page.GetByText("Complete succeeded.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });

            dialog = DetailDialog(appointment.Id);
            await Expect(dialog.GetByText("Completed")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" })).ToHaveCountAsync(0);
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" })).ToHaveCountAsync(0);
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Check in" })).ToHaveCountAsync(0);

            (await DoctorE2eApi.CountNotesForAppointmentAsync(Host, appointment.Id)).Should().Be(2);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Note_Lifecycle_And_Complete_Own_Appointment));
            throw;
        }
    }

    private async Task OpenOwnAppointmentDetailAsync(AppointmentResponse appointment)
    {
        await Page.GotoAsync("/appointments");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Appointment Queue" }))
            .ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = $"Details for appointment {appointment.Id}" }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Details for appointment {appointment.Id}" })
            .ClickAsync();
        await Expect(DetailDialog(appointment.Id)).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    private ILocator DetailDialog(Guid appointmentId) =>
        Page.Locator(".ant-modal").Filter(new() { HasText = appointmentId.ToString("D") });
}
