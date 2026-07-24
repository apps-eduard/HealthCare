using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public abstract class E2ePageTestBase : PageTest
{
    protected E2eHostFixture Host { get; }

    protected E2ePageTestBase(E2eHostFixture host)
    {
        Host = host;
    }

    public override BrowserNewContextOptions ContextOptions()
    {
        return new BrowserNewContextOptions
        {
            BaseURL = Host.WebBaseUrl,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        };
    }

    protected async Task LoginAsAsync(string email, string password)
    {
        await Page.GotoAsync("/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Page.Locator("#bff-email").FillAsync(email);
        await Page.Locator("#bff-password").FillAsync(password);
        await Task.WhenAll(
            Page.WaitForURLAsync(
                url => url.Contains("/dashboard", StringComparison.OrdinalIgnoreCase)
                       || url.Contains("/forbidden", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = 90_000 }),
            Page.Locator("#bff-login-submit").ClickAsync());
    }

    protected async Task LoginAsOrganizationAdminAsync() =>
        await LoginAsAsync(Host.Users.OrganizationAdminEmail, Host.Users.OrganizationAdminPassword);

    protected async Task LogoutAsync()
    {
        await Page.GotoAsync("/logout");
        await Page.WaitForURLAsync(
            url => url.Contains("/login", StringComparison.OrdinalIgnoreCase),
            new PageWaitForURLOptions { Timeout = 60_000 });
    }

    /// <summary>
    /// Org Admin nav children live under collapsed Ant Design SubMenus; expand before asserting links.
    /// </summary>
    protected async Task AssertOrganizationAdminNavigationAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Governance" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Organization" })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Organization" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Organization Profile" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Governance" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Audit Logs" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Usage & Limits" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    protected async Task OnFailureCaptureAsync(string testName)
    {
        await Host.CaptureFailureArtifactsAsync(Page, testName);
    }
}
