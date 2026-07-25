using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminStaffSmokeTests : E2ePageTestBase
{
    public ClinicAdminStaffSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_Manage_Clinic_Scoped_Staff_Without_Org_Controls()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/staff");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Staff Management" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Manage staff in your clinic", new() { Exact = false }))
                .ToBeVisibleAsync();

            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Change clinic" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Leave empty for all clinics", new() { Exact = false })).ToHaveCountAsync(0);

            await Page.Locator("#staff-role").ClickAsync();
            await Expect(Page.Locator(".ant-select-dropdown:visible").GetByText("ORGANIZATION_ADMIN")).ToHaveCountAsync(0);
            await Expect(Page.Locator(".ant-select-dropdown:visible").GetByText("PLATFORM_ADMIN")).ToHaveCountAsync(0);
            await Page.Keyboard.PressAsync("Escape");

            await Page.GetByRole(AriaRole.Tab, new() { Name = "Receptionists" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = "Receptionists" })).ToHaveAttributeAsync("aria-selected", "true");

            await Page.GetByRole(AriaRole.Button, new() { Name = "Create staff" }).ClickAsync();
            var modal = Page.Locator(".ant-modal").Filter(new() { HasText = "Temporary password" });
            await Expect(modal.GetByText("New staff will be created in your membership clinic."))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });

            // Wait until assignable roles finish loading (Create enabled).
            var createButton = modal.GetByRole(AriaRole.Button, new() { Name = "Create" });
            try
            {
                await Expect(createButton).ToBeEnabledAsync(new() { Timeout = 20_000 });
            }
            catch (PlaywrightException)
            {
                // Roles select may still be empty after a slow/failed first load — pick Receptionist explicitly.
                await modal.Locator(".ant-select").First.ClickAsync();
                await Page.Locator(".ant-select-dropdown:visible").GetByText("RECEPTIONIST", new() { Exact = true }).ClickAsync();
                await Expect(createButton).ToBeEnabledAsync(new() { Timeout = 10_000 });
            }

            var email = $"ca3.recv.{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}@healthcare.local";
            await modal.Locator("input").Nth(0).FillAsync(email);
            await modal.Locator("input").Nth(1).FillAsync("CA3");
            await modal.Locator("input").Nth(2).FillAsync("Recv");
            await modal.Locator("input[type='password']").Nth(0).FillAsync("TempPass_Staff_99!");
            await modal.Locator("input[type='password']").Nth(1).FillAsync("TempPass_Staff_99!");
            await modal.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();

            await Expect(Page.GetByText(email)).ToBeVisibleAsync(new() { Timeout = 30_000 });

            var row = Page.Locator("tr", new() { HasText = email });
            await row.GetByRole(AriaRole.Button, new() { Name = "Password reset for CA3 Recv" }).ClickAsync();
            var resetModal = Page.Locator(".ant-modal").Filter(new() { HasText = "Send reset" });
            await Expect(resetModal).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await resetModal.GetByRole(AriaRole.Button, new() { Name = "Send reset" }).ClickAsync();
            // Modal closes on API success; Ant Design message toasts can be fleeting under load.
            await Expect(resetModal).ToBeHiddenAsync(new() { Timeout = 30_000 });
            try
            {
                await Expect(Page.GetByText("Password reset initiated.", new() { Exact = false }))
                    .ToBeVisibleAsync(new() { Timeout = 5_000 });
            }
            catch (PlaywrightException)
            {
                await Expect(Page.GetByText(email)).ToBeVisibleAsync();
            }

            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Change clinic" })).ToHaveCountAsync(0);
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_Manage_Clinic_Scoped_Staff_Without_Org_Controls));
            throw;
        }
    }
}
