using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using Microsoft.Playwright;

namespace HealthCare.EndToEndTests;

/// <summary>
/// CA-10 hardening pack: logout, concurrency, cross-clinic API denial, org-route denial, narrow viewport.
/// </summary>
[Collection(E2eCollection.Name)]
public sealed class ClinicAdminHardeningSmokeTests : E2ePageTestBase
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ClinicAdminHardeningSmokeTests(E2eHostFixture host)
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

            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page).ToHaveURLAsync(new Regex(".*/dashboard.*"));
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Dashboard" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await LogoutAsync();
            await Expect(Page).ToHaveURLAsync(new Regex(".*/login.*"));

            await Page.GotoAsync("/clinic/settings");
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
    public async Task Clinic_Profile_Concurrency_Shows_Reload_Guidance()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Page.GotoAsync("/clinic/settings");
            await Expect(Page.Locator("#clinic-settings-phone")).ToBeVisibleAsync(new() { Timeout = 60_000 });

            var content = await Page.ContentAsync();
            var versionMatch = Regex.Match(content, @"Version\s+(\d+)");
            versionMatch.Success.Should().BeTrue("clinic settings page should show Version N");
            var expectedVersion = int.Parse(versionMatch.Groups[1].Value);

            await Page.Locator("#clinic-settings-phone").FillAsync($"+9665{Random.Shared.Next(10000000, 99999999)}");

            using var api = new HttpClient { BaseAddress = new Uri(Host.ApiBaseUrl.TrimEnd('/') + "/") };
            var login = await api.PostAsJsonAsync(
                "api/v1/auth/login",
                new LoginRequest
                {
                    Email = Host.Users.ClinicAdminEmail,
                    Password = Host.Users.ClinicAdminPassword,
                });
            login.EnsureSuccessStatusCode();
            var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
            api.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

            var competing = await api.PatchAsJsonAsync(
                "api/v1/clinic/settings",
                new UpdateClinicSettingsRequest
                {
                    ExpectedVersion = expectedVersion,
                    ContactPhone = $"+9665{Random.Shared.Next(10000000, 99999999)}",
                },
                PatchJsonOptions);
            competing.EnsureSuccessStatusCode();

            await Page.GetByRole(AriaRole.Button, new() { Name = "Save clinic profile" }).ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reload latest clinic profile" }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.GetByText("Another change was saved first. Reload the latest profile and try again."))
                .ToBeVisibleAsync();
            await Expect(Page.Locator("#blazor-error-ui.show")).ToHaveCountAsync(0);
            var body = await Page.ContentAsync();
            body.Should().NotContain("StackTrace");
            body.Should().NotContain("at HealthCare.");
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Clinic_Profile_Concurrency_Shows_Reload_Guidance));
            throw;
        }
    }

    [Fact]
    public async Task Cross_Clinic_Api_And_Org_Routes_Are_Denied()
    {
        try
        {
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Dashboard" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            foreach (var path in new[]
                     {
                         "/organization/settings",
                         "/reports",
                         "/audit-logs",
                         "/usage",
                         "/security",
                     })
            {
                await Page.GotoAsync(path);
                await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                var content = await Page.ContentAsync();
                (Page.Url.Contains("/forbidden", StringComparison.OrdinalIgnoreCase)
                 || content.Contains("permission", StringComparison.OrdinalIgnoreCase)
                 || content.Contains("do not have permission", StringComparison.OrdinalIgnoreCase)
                 || content.Contains("Select an organization", StringComparison.OrdinalIgnoreCase))
                    .Should().BeTrue($"Clinic Admin must be denied {path}");
            }

            await Expect(Page.GetByText("Platform context:")).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Select organization" })).ToHaveCountAsync(0);
            await Expect(Page.GetByText("Medical note", new() { Exact = false })).ToHaveCountAsync(0);

            var foreignClinicId = Guid.NewGuid();
            using var api = new HttpClient { BaseAddress = new Uri(Host.ApiBaseUrl.TrimEnd('/') + "/") };
            var login = await api.PostAsJsonAsync(
                "api/v1/auth/login",
                new LoginRequest
                {
                    Email = Host.Users.ClinicAdminEmail,
                    Password = Host.Users.ClinicAdminPassword,
                });
            login.EnsureSuccessStatusCode();
            var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
            api.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

            var dashboard = await api.GetAsync($"api/v1/clinic/dashboard?clinicId={foreignClinicId:D}");
            ((int)dashboard.StatusCode).Should().BeOneOf(403, 404);

            var settings = await api.GetAsync($"api/v1/clinic/settings?clinicId={foreignClinicId:D}");
            ((int)settings.StatusCode).Should().BeOneOf(403, 404);

            var reports = await api.GetAsync($"api/v1/clinic/reports/appointments?clinicId={foreignClinicId:D}");
            ((int)reports.StatusCode).Should().BeOneOf(403, 404);

            var audit = await api.GetAsync($"api/v1/clinic/audit-logs?clinicId={foreignClinicId:D}");
            ((int)audit.StatusCode).Should().BeOneOf(403, 404);

            var orgSettings = await api.GetAsync("api/v1/organization/settings");
            ((int)orgSettings.StatusCode).Should().BeOneOf(401, 403);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Cross_Clinic_Api_And_Org_Routes_Are_Denied));
            throw;
        }
    }

    [Fact]
    public async Task Narrow_Viewport_Navigation_And_Clinic_Profile_Remain_Usable()
    {
        try
        {
            await Page.SetViewportSizeAsync(390, 844);
            await LoginAsAsync(Host.Users.ClinicAdminEmail, Host.Users.ClinicAdminPassword);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Dashboard" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });

            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" }))
                .ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" }).ClickAsync();

            await Page.GotoAsync("/clinic/settings");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Clinic Profile" }))
                .ToBeVisibleAsync(new() { Timeout = 60_000 });
            await Expect(Page.Locator("#clinic-settings-name")).ToBeVisibleAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save clinic profile" }))
                .ToBeVisibleAsync();
            await Expect(Page.Locator("label", new() { HasText = "Clinic filter" })).ToHaveCountAsync(0);
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Export CSV" })).ToHaveCountAsync(0);
        }
        catch
        {
            await OnFailureCaptureAsync(nameof(Narrow_Viewport_Navigation_And_Clinic_Profile_Remain_Usable));
            throw;
        }
    }
}
