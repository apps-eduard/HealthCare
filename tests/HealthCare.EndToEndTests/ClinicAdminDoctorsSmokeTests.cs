using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminDoctorsSmokeTests : E2ePageTestBase
{
    public ClinicAdminDoctorsSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_Manage_Doctor_Availability_From_Directory()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/doctors");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Doctors" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Doctors in your clinic", new() { Exact = false }))
                .ToBeVisibleAsync();

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);

            await Expect(Page.GetByRole(AriaRole.Table, new() { Name = "Clinic doctors" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.Locator("table.hc-table tbody tr").First).ToBeVisibleAsync();

            await Page.Locator("button[aria-label^='Manage availability for']").First.ClickAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/availability\\?doctorId="));
            var availabilityUrl = Page.Url;

            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Doctor Availability" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.Locator("[data-testid='availability-clinic-caption']")).ToBeVisibleAsync();
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);

            var exceptionsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Exceptions" });
            if (!await exceptionsTab.IsVisibleAsync())
            {
                await Page.GetByLabel("Select doctor").ClickAsync();
                await Expect(Page.Locator(".ant-select-dropdown:visible .ant-select-item-option").First)
                    .ToBeVisibleAsync(new() { Timeout = 20_000 });
                await Page.Locator(".ant-select-dropdown:visible .ant-select-item-option").First.ClickAsync();
            }

            await Expect(exceptionsTab).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await exceptionsTab.ClickAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Add availability exception" }).ClickAsync();

            var modal = Page.Locator(".ant-modal").Filter(new() { HasText = "Add exception" });
            await Expect(modal).ToBeVisibleAsync(new() { Timeout = 15_000 });
            var reason = $"CA4 exception {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            await modal.GetByRole(AriaRole.Textbox).Last.FillAsync(reason);
            await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

            await Expect(Page.GetByText(reason)).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.GotoAsync(availabilityUrl);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Doctor Availability" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            exceptionsTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Exceptions" });
            if (!await exceptionsTab.IsVisibleAsync())
            {
                await Page.GetByLabel("Select doctor").ClickAsync();
                await Expect(Page.Locator(".ant-select-dropdown:visible .ant-select-item-option").First)
                    .ToBeVisibleAsync(new() { Timeout = 20_000 });
                await Page.Locator(".ant-select-dropdown:visible .ant-select-item-option").First.ClickAsync();
            }

            await Expect(exceptionsTab).ToBeVisibleAsync(new() { Timeout = 45_000 });
            await exceptionsTab.ClickAsync();
            await Expect(Page.GetByText(reason)).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_Manage_Doctor_Availability_From_Directory));
            throw;
        }
    }
}
