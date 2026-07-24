using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class ClinicAdminDashboardSmokeTests : E2ePageTestBase
{
    public ClinicAdminDashboardSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Clinic_Admin_Sees_Clinic_Dashboard_And_Cannot_Open_Organization_Settings()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Dashboard" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.Locator(".ant-statistic-title", new() { HasText = "Active staff" })).ToBeVisibleAsync();
            await Expect(Page.Locator(".ant-statistic-title", new() { HasText = "Doctors" })).ToBeVisibleAsync();
            await Expect(Page.Locator(".ant-statistic-title", new() { HasText = "Patients" })).ToBeVisibleAsync();
            await Expect(Page.Locator(".ant-statistic-title", new() { HasText = "Today’s appointments" })).ToBeVisibleAsync();

            await Expect(Page.GetByText("Platform context:")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Select organization" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Usage & Limits" })).ToHaveCountAsync(0);

            await Page.GotoAsync("/organization/settings");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var content = await Page.ContentAsync();
            (Page.Url.Contains("/forbidden", StringComparison.OrdinalIgnoreCase)
             || content.Contains("permission", StringComparison.OrdinalIgnoreCase)
             || content.Contains("do not have permission", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue();
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Sees_Clinic_Dashboard_And_Cannot_Open_Organization_Settings));
            throw;
        }
    }
}
