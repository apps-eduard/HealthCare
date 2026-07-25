using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

/// <summary>
/// DR-10: completion does not require or auto-create a medical note.
/// </summary>
[Collection(E2eCollection.Name)]
public sealed class DoctorCompletionWithoutNoteSmokeTests : E2ePageTestBase
{
    public DoctorCompletionWithoutNoteSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Doctor_Can_Complete_Appointment_Without_Medical_Note()
    {
        try
        {
            var appointment = await DoctorE2eApi.CreateCheckedInOwnAppointmentAsync(Host, "DR10-NONE");
            (await DoctorE2eApi.CountNotesForAppointmentAsync(Host, appointment.Id)).Should().Be(0);

            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Page.GotoAsync("/appointments");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Appointment Queue" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Page.GetByRole(AriaRole.Button, new() { Name = $"Details for appointment {appointment.Id}" })
                .ClickAsync();
            var dialog = Page.Locator(".ant-modal").Filter(new() { HasText = appointment.Id.ToString("D") });
            await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(dialog.GetByText("No notes authored by you", new() { Exact = false })).ToBeVisibleAsync();

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" }).ClickAsync();
            var completeConfirm = Page.Locator(".ant-modal").Filter(new() { HasText = "Complete" });
            await Expect(completeConfirm.GetByText("medical note is not required", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 10_000 });
            await completeConfirm.GetByRole(AriaRole.Button, new() { Name = "OK" }).ClickAsync();

            await Expect(Page.GetByText("Complete succeeded.", new() { Exact = false }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            dialog = Page.Locator(".ant-modal").Filter(new() { HasText = appointment.Id.ToString("D") });
            await Expect(dialog.GetByText("Completed")).ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" })).ToHaveCountAsync(0);

            (await DoctorE2eApi.CountNotesForAppointmentAsync(Host, appointment.Id)).Should().Be(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Can_Complete_Appointment_Without_Medical_Note));
            throw;
        }
    }
}
