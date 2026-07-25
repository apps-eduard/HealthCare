using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

/// <summary>
/// DR-10: narrow-viewport smoke for Doctor appointment detail + note actions.
/// </summary>
[Collection(E2eCollection.Name)]
public sealed class DoctorResponsiveWorkflowSmokeTests : E2ePageTestBase
{
    public DoctorResponsiveWorkflowSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Doctor_Workflow_Remains_Usable_At_Narrow_Viewport()
    {
        try
        {
            var appointment = await DoctorE2eApi.CreateCheckedInOwnAppointmentAsync(Host, "DR10-NARROW");

            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Page.SetViewportSizeAsync(390, 844);

            var toggle = Page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" });
            if (await toggle.CountAsync() > 0 && await toggle.IsVisibleAsync())
            {
                await toggle.ClickAsync();
            }

            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Clinic Reports" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Staff Management" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Clinic Audit Logs" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Doctor Reports" })).ToHaveCountAsync(0);

            await Page.GotoAsync("/appointments");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Appointment Queue" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Page.GetByRole(AriaRole.Button, new() { Name = $"Details for appointment {appointment.Id}" })
                .ClickAsync();
            var dialog = Page.Locator(".ant-modal").Filter(new() { HasText = appointment.Id.ToString("D") });
            await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 20_000 });

            var box = await dialog.BoundingBoxAsync();
            box.Should().NotBeNull();
            box!.Width.Should().BeLessThanOrEqualTo(390 + 8);
            box.X.Should().BeGreaterThanOrEqualTo(-8);

            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Create medical note draft" })
                    .Or(dialog.GetByRole(AriaRole.Button, new() { Name = "New draft note" })))
                .ToBeVisibleAsync(new() { Timeout = 20_000 });
            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" })).ToBeVisibleAsync();
            await Expect(dialog.Locator("button.ant-btn").Filter(new() { HasText = "Close" })).ToBeVisibleAsync();

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" }).ClickAsync();
            var completeConfirm = Page.Locator(".ant-modal")
                .Filter(new() { HasText = "medical note is not required" });
            await Expect(completeConfirm).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Expect(completeConfirm.GetByRole(AriaRole.Button, new() { Name = "OK" })).ToBeVisibleAsync();
            await completeConfirm.Locator("button.ant-btn-default").Filter(new() { HasText = "Cancel" }).ClickAsync();

            await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Complete" })).ToBeVisibleAsync();
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Workflow_Remains_Usable_At_Narrow_Viewport));
            throw;
        }
    }
}
