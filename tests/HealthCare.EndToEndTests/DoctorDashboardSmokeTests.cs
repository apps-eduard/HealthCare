using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class DoctorDashboardSmokeTests : E2ePageTestBase
{
    public DoctorDashboardSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Doctor_Sees_Doctor_Dashboard_And_Cannot_Open_Organization_Settings()
    {
        try
        {
            await LoginAsAsync(Host.Users.DoctorEmail, Host.Users.DoctorPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Doctor Dashboard" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.Locator("[data-testid='doctor-dashboard-caption']")).ToBeVisibleAsync();
            await Expect(Page.Locator(".ant-statistic-title", new() { HasText = "Today’s appointments" }))
                .ToBeVisibleAsync();
            await Expect(Page.Locator(".ant-statistic-title", new() { HasText = "Upcoming appointments" }))
                .ToBeVisibleAsync();
            await Expect(Page.Locator(".ant-statistic-title", new() { HasText = "Availability warnings" }))
                .ToBeVisibleAsync();

            await Expect(Page.GetByText("Platform context:")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Select organization" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Organization Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Usage & Limits" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Clinic Profile" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Staff Management" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Patients" })).ToBeVisibleAsync();
            await Expect(Page.GetByText("Medical Notes", new() { Exact = false })).ToHaveCountAsync(0);

            await Page.GotoAsync("/organization/settings");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var content = await Page.ContentAsync();
            (Page.Url.Contains("/forbidden", StringComparison.OrdinalIgnoreCase)
             || content.Contains("permission", StringComparison.OrdinalIgnoreCase)
             || content.Contains("do not have permission", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue();

            await LogoutAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/login.*"));
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Doctor_Sees_Doctor_Dashboard_And_Cannot_Open_Organization_Settings));
            throw;
        }
    }
}
