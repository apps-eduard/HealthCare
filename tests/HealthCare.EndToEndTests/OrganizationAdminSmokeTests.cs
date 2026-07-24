using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

[Collection(E2eCollection.Name)]
public sealed class OrganizationAdminSmokeTests : E2ePageTestBase
{
    public OrganizationAdminSmokeTests(E2eHostFixture host)
        : base(host)
    {
    }

    [Fact]
    public async Task Login_Logout_And_Protected_Route()
    {
        try
        {
            await Page.GotoAsync("/login");
            await Expect(Page.Locator("#bff-login-form")).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "HealthCare" })).ToBeVisibleAsync();

            await LoginAsOrganizationAdminAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));
            await Expect(Page.GetByText("Organization Dashboard")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Organization Profile")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Audit Logs")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Usage & Limits")).ToBeVisibleAsync();

            await LogoutAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/login.*"));

            await Page.GotoAsync("/organization/settings");
            await Page.WaitForURLAsync(
                url => url.Contains("/login", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = 60_000 });
            Page.Url.Should().Contain("/login");
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Login_Logout_And_Protected_Route));
            throw;
        }
    }

    [Fact]
    public async Task Dashboard_Shows_Org_Admin_Navigation_Without_Platform_Controls()
    {
        try
        {
            await LoginAsOrganizationAdminAsync();
            await Expect(Page.GetByText("Organization Dashboard")).ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.GetByText("Organization Profile")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Audit Logs")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Usage & Limits")).ToBeVisibleAsync();

            await Expect(Page.GetByText("Platform context:")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Select organization" })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Dashboard_Shows_Org_Admin_Navigation_Without_Platform_Controls));
            throw;
        }
    }

    [Fact]
    public async Task Organization_Profile_Can_Update_And_Persists()
    {
        try
        {
            await LoginAsOrganizationAdminAsync();
            await Page.GotoAsync("/organization/settings");
            await Expect(Page.GetByText("Organization Profile")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Status is read-only")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Max clinics")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Max staff")).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Open usage and limits" })).ToBeVisibleAsync();

            var html = (await Page.ContentAsync()).ToLowerInvariant();
            html.Should().NotContain("billing");
            html.Should().NotContain("delete organization");
            html.Should().NotContain("suspend organization");

            var marker = $"e2e{Guid.NewGuid():N}"[..12];
            await Page.Locator("#org-settings-email").FillAsync($"{marker}@example.com");
            await Page.Locator("#org-settings-country").FillAsync("SA");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save organization profile" }).ClickAsync();

            await Expect(Page.GetByText("Organization profile saved.")).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.ReloadAsync();
            await Expect(Page.Locator("#org-settings-email")).ToHaveValueAsync($"{marker}@example.com");
            await Expect(Page.Locator("#org-settings-country")).ToHaveValueAsync("SA");

            await Page.GetByRole(AriaRole.Button, new() { Name = "Open usage and limits" }).ClickAsync();
            await Page.WaitForURLAsync(url => url.Contains("/usage", StringComparison.OrdinalIgnoreCase));
            await Expect(Page.GetByText("Usage & Limits")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Organization_Profile_Can_Update_And_Persists));
            throw;
        }
    }

    [Fact]
    public async Task Organization_Profile_Concurrency_Shows_Reload_Guidance()
    {
        try
        {
            await LoginAsOrganizationAdminAsync();
            await Page.GotoAsync("/organization/settings");
            await Expect(Page.Locator("#org-settings-email")).ToBeVisibleAsync(new() { Timeout = 60_000 });

            await using var competing = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = Host.WebBaseUrl,
                IgnoreHTTPSErrors = true,
            });
            var pageB = await competing.NewPageAsync();
            await pageB.GotoAsync("/login");
            await pageB.Locator("#bff-email").FillAsync(Host.Users.OrganizationAdminEmail);
            await pageB.Locator("#bff-password").FillAsync(Host.Users.OrganizationAdminPassword);
            await pageB.Locator("#bff-login-submit").ClickAsync();
            await pageB.WaitForURLAsync(url => url.Contains("/dashboard", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = 60_000 });
            await pageB.GotoAsync("/organization/settings");
            await Expect(pageB.Locator("#org-settings-phone")).ToBeVisibleAsync(new() { Timeout = 60_000 });

            var phoneA = $"+9665{Random.Shared.Next(10000000, 99999999)}";
            var phoneB = $"+9665{Random.Shared.Next(10000000, 99999999)}";

            await Page.Locator("#org-settings-phone").FillAsync(phoneA);
            await pageB.Locator("#org-settings-phone").FillAsync(phoneB);

            await pageB.GetByRole(AriaRole.Button, new() { Name = "Save organization profile" }).ClickAsync();
            await Expect(pageB.GetByText("Organization profile saved.")).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.GetByRole(AriaRole.Button, new() { Name = "Save organization profile" }).ClickAsync();
            await Expect(Page.GetByText("Reload")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            var body = await Page.ContentAsync();
            body.Should().NotContain("StackTrace");
            body.Should().NotContain("at HealthCare.");
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reload latest organization profile" }))
                .ToBeVisibleAsync();
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Organization_Profile_Concurrency_Shows_Reload_Guidance));
            throw;
        }
    }

    [Fact]
    public async Task Audit_Logs_Show_Profile_Update_Without_Unsafe_Controls()
    {
        try
        {
            await LoginAsOrganizationAdminAsync();
            await Page.GotoAsync("/organization/settings");
            await Expect(Page.Locator("#org-settings-branding")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            var branding = $"Brand-{Guid.NewGuid():N}"[..12];
            await Page.Locator("#org-settings-branding").FillAsync(branding);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Save organization profile" }).ClickAsync();
            await Expect(Page.GetByText("Organization profile saved.")).ToBeVisibleAsync(new() { Timeout = 30_000 });

            await Page.GotoAsync("/audit-logs");
            await Expect(Page.GetByText("Audit Logs")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Page.Locator("#audit-action").FillAsync("organization_profile_update");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Apply audit filters" }).ClickAsync();
            await Expect(Page.GetByText("organization_profile_update")).ToBeVisibleAsync(new() { Timeout = 60_000 });

            var content = (await Page.ContentAsync()).ToLowerInvariant();
            content.Should().NotContain("delete audit");
            content.Should().NotContain("edit audit");
            content.Should().NotContain("stacktrace");
            content.Should().NotContain("requestbody");
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Audit_Logs_Show_Profile_Update_Without_Unsafe_Controls));
            throw;
        }
    }

    [Fact]
    public async Task Usage_Limits_Are_Read_Only()
    {
        try
        {
            await LoginAsOrganizationAdminAsync();
            await Page.GotoAsync("/usage");
            await Expect(Page.GetByText("Usage & Limits")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Page.GetByRole(AriaRole.Button, new() { Name = "Load usage" }).ClickAsync();
            await Expect(Page.GetByText("Max clinics")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("Max staff")).ToBeVisibleAsync();
            await Expect(Page.GetByText("Remaining")).ToBeVisibleAsync();

            var content = (await Page.ContentAsync()).ToLowerInvariant();
            content.Should().Contain("cannot be changed");
            content.Should().NotContain("billing");
            content.Should().NotContain("checkout");
            content.Should().NotContain("increase limit");
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Usage_Limits_Are_Read_Only));
            throw;
        }
    }

    [Fact]
    public async Task Clinic_Admin_Is_Denied_Organization_Profile()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Page.GotoAsync("/organization/settings");
            await Expect(Page.GetByText("You do not have permission to view organization profile settings."))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save organization profile" })).ToHaveCountAsync(0);

            using var client = new HttpClient { BaseAddress = new Uri(Host.ApiBaseUrl + "/") };
            var login = await client.PostAsJsonAsync(
                "api/v1/auth/login",
                new { email = Host.Users.ClinicAdminEmail, password = Host.Users.ClinicAdminPassword });
            login.EnsureSuccessStatusCode();
            var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

            var patch = await client.PatchAsJsonAsync(
                "api/v1/organization/settings",
                new { expectedVersion = 0, name = "Should Fail" });
            ((int)patch.StatusCode).Should().BeOneOf(401, 403);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Admin_Is_Denied_Organization_Profile));
            throw;
        }
    }

    [Fact]
    public async Task Patient_Cannot_Access_Organization_Admin_Routes()
    {
        try
        {
            await LoginAsAsync(Host.Users.PatientEmail, Host.Users.PatientPassword);
            Page.Url.Should().Contain("/forbidden");

            await Page.GotoAsync("/organization/settings");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var url = Page.Url;
            var content = await Page.ContentAsync();
            (url.Contains("/forbidden", StringComparison.OrdinalIgnoreCase)
             || url.Contains("/login", StringComparison.OrdinalIgnoreCase)
             || content.Contains("Patient accounts", StringComparison.OrdinalIgnoreCase)
             || content.Contains("permission", StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue();
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Patient_Cannot_Access_Organization_Admin_Routes));
            throw;
        }
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Organization_Selection()
    {
        try
        {
            await LoginAsAsync(Host.Users.PlatformAdminEmail, Host.Users.PlatformAdminPassword);
            await Expect(Page.GetByText("Platform context:")).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByText("(none selected)")).ToBeVisibleAsync();

            await Page.GotoAsync("/organization/settings");
            await Expect(Page.GetByText("Select an organization in the platform banner before loading profile settings."))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Select organization" })).ToBeVisibleAsync();
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Platform_Admin_Requires_Explicit_Organization_Selection));
            throw;
        }
    }
}
