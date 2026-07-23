using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using HealthCare.Web.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HealthCare.Web.Tests;

/// <summary>
/// HTTP-level BFF auth hardening: methods, antiforgery, and non-mutating GETs.
/// </summary>
public sealed class BffAuthCsrfEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public BffAuthCsrfEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(_ => { });
    }

    [Fact]
    public async Task Get_Establish_Is_Method_Not_Allowed()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/bff/auth/establish");
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Get_Logout_Is_Method_Not_Allowed()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/bff/auth/logout");
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Post_Establish_Is_Method_Not_Allowed()
    {
        var client = CreateClient();
        var noBody = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/bff/auth/establish"));
        noBody.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        var form = await client.PostAsync("/bff/auth/establish", new FormUrlEncodedContent([]));
        form.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        var setCookie = form.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join(';', cookies)
            : string.Empty;
        setCookie.Should().NotContain("HealthCare.Staff.Auth");
        setCookie.Should().NotContain("__Host-HealthCare.Staff");
    }

    [Fact]
    public async Task Login_Without_Antiforgery_Is_Rejected()
    {
        var client = CreateClient();
        var response = await client.PostAsync(
            "/bff/auth/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "anyone@test.local",
                ["password"] = "not-a-real-password",
                ["returnUrl"] = "/appointments",
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("error=antiforgery");
    }

    [Fact]
    public async Task Login_With_Invalid_Antiforgery_Is_Rejected()
    {
        var client = CreateClient();
        await client.GetAsync("/login");

        var response = await client.PostAsync(
            "/bff/auth/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "anyone@test.local",
                ["password"] = "not-a-real-password",
                ["returnUrl"] = "/appointments",
                ["__RequestVerificationToken"] = "not-a-valid-token",
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("error=antiforgery");
    }

    [Fact]
    public async Task Logout_Without_Antiforgery_Is_Rejected()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/bff/auth/logout", new FormUrlEncodedContent([]));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("error=antiforgery");
    }

    [Fact]
    public async Task Logout_With_Valid_Antiforgery_Succeeds_Idempotently()
    {
        var client = CreateClient();
        var token = await ObtainAntiforgeryTokenAsync(client);

        var response = await client.PostAsync(
            "/bff/auth/logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/login");

        var token2 = await ObtainAntiforgeryTokenAsync(client);
        var second = await client.PostAsync(
            "/bff/auth/logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token2,
            }));
        second.StatusCode.Should().Be(HttpStatusCode.Redirect);
        second.Headers.Location!.ToString().Should().Be("/login");
    }

    [Fact]
    public async Task Valid_Antiforgery_Allows_Login_Attempt_Without_Crashing()
    {
        var client = CreateClient();
        var token = await ObtainAntiforgeryTokenAsync(client);

        var response = await client.PostAsync(
            "/bff/auth/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "nobody@test.local",
                ["password"] = "wrong-password",
                ["returnUrl"] = "/appointments",
                ["__RequestVerificationToken"] = token,
            }));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/login");
        location.Should().NotContain("error=antiforgery");
    }

    [Fact]
    public void No_Login_Ticket_Cookie_Is_Issued_By_Design()
    {
        // One-step POST login establishes the session; no separate ticket cookie exists.
        typeof(IApiTokenSessionStore).GetMethods()
            .Select(m => m.Name)
            .Should()
            .NotContain(n => n.Contains("Ticket", StringComparison.OrdinalIgnoreCase));

        BffCookieNames.LegacyLoginTicket.Should().Be("HealthCare.Staff.LoginTicket");
        BffCookieNames.LegacyLoginCorrelation.Should().Be("HealthCare.Staff.LoginCorrelation");
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<string> ObtainAntiforgeryTokenAsync(HttpClient client)
    {
        // Logout page renders AntiforgeryToken in a simple non-interactive form.
        var page = await client.GetAsync("/logout");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();

        var tokenMatch = Regex.Match(
            html,
            @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
            RegexOptions.IgnoreCase);
        if (!tokenMatch.Success)
        {
            tokenMatch = Regex.Match(
                html,
                @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""",
                RegexOptions.IgnoreCase);
        }

        tokenMatch.Success.Should().BeTrue("logout page should render an antiforgery token");
        return tokenMatch.Groups[1].Value;
    }
}
