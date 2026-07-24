using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminSettingsSmokeTests : E2ePageTestBase
{
    public ClinicAdminSettingsSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Can_Edit_Clinic_Profile_And_See_Persistence()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));

            await Page.GotoAsync("/clinic/settings");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Profile" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.GetByText("Status is read-only")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Slug:", new() { Exact = false })).ToBeVisibleAsync();
            await Expect(Page.GetByText("Organization:", new() { Exact = false })).ToBeVisibleAsync();

            await Expect(Page.GetByText("Max clinics", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Max staff", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("billing", new() { Exact = false })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Delete" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Activate" })).ToHaveCountAsync(0);

            var unique = $"CA2 Profile {DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var nameInput = Page.Locator("#clinic-settings-name");
            await Expect(nameInput).ToBeEnabledAsync();
            await nameInput.FillAsync(unique);

            await Page.GetByRole(AriaRole.Button, new() { Name = "Save clinic profile" }).ClickAsync();
            await Expect(Page.GetByText("Clinic profile saved.")).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.ReloadAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Profile" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.Locator("#clinic-settings-name")).ToHaveValueAsync(unique);

            await Expect(Page.GetByText("Status is read-only")).ToBeVisibleAsync();
            await Expect(Page.GetByText("(read-only)", new() { Exact = false })).ToBeVisibleAsync();
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Can_Edit_Clinic_Profile_And_See_Persistence));
            throw;
        }
    }
}
