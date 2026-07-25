using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class AuthenticatingHttpMessageHandlerTests
{
    [Fact]
    public async Task SendAsync_Attaches_Bearer_Access_Token()
    {
        var session = await CreateSessionAsync("access-token", "refresh-token");
        string? seenAuth = null;
        var inner = new StubHandler(request =>
        {
            seenAuth = request.Headers.Authorization?.ToString();
            return Ok();
        });

        using var response = await SendAsync(session, new FakeTokenRefresher(_ => Task.FromResult(false)), inner, "api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        seenAuth.Should().Be("Bearer access-token");
        inner.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_On_401_Refreshes_Once_And_Retries()
    {
        var session = await CreateSessionAsync("old-access", "refresh-1");
        var refresher = new FakeTokenRefresher(async _ =>
        {
            await session.SetSessionAsync(CreateTokens("new-access", "refresh-2"));
            return true;
        });

        var authHeaders = new List<string?>();
        var inner = new StubHandler(request =>
        {
            authHeaders.Add(request.Headers.Authorization?.Parameter);
            return authHeaders.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Ok();
        });

        using var response = await SendAsync(session, refresher, inner, "api/v1/patients/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        refresher.CallCount.Should().Be(1);
        inner.SendCount.Should().Be(2);
        authHeaders.Should().Equal("old-access", "new-access");
        session.Current.AccessToken.Should().Be("new-access");
    }

    [Fact]
    public async Task SendAsync_On_Refresh_Failure_Clears_Session()
    {
        var session = await CreateSessionAsync("old-access", "refresh-1");
        var refresher = new FakeTokenRefresher(_ => Task.FromResult(false));
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var response = await SendAsync(session, refresher, inner, "api/v1/patients/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        session.IsAuthenticated.Should().BeFalse();
        refresher.CallCount.Should().Be(1);
        inner.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_Does_Not_Refresh_Auth_Endpoints()
    {
        var session = await CreateSessionAsync("access", "refresh");
        var refresher = new FakeTokenRefresher(_ => Task.FromResult(true));
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var handler = CreateHandler(session, refresher, inner);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        using var response = await client.PostAsync("api/v1/auth/login", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresher.CallCount.Should().Be(0);
        session.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_Prevents_Infinite_Refresh_Loop_By_Retrying_Only_Once()
    {
        var session = await CreateSessionAsync("access-1", "refresh-1");
        var refresher = new FakeTokenRefresher(async _ =>
        {
            await session.SetSessionAsync(CreateTokens("access-2", "refresh-2"));
            return true;
        });

        // First call 401, refresh+retry also 401 — handler must not refresh again.
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using var response = await SendAsync(session, refresher, inner, "api/v1/patients/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refresher.CallCount.Should().Be(1);
        inner.SendCount.Should().Be(2);
        session.IsAuthenticated.Should().BeTrue();
        session.Current.AccessToken.Should().Be("access-2");
    }

    [Fact]
    public async Task SendAsync_Skips_Refresh_When_Concurrent_Refresh_Already_Rotated_Token()
    {
        var session = await CreateSessionAsync("old-access", "refresh-1");
        var refresher = new FakeTokenRefresher(_ => Task.FromResult(true));
        var call = 0;
        var inner = new StubHandler(_ =>
        {
            call++;
            if (call == 1)
            {
                // Concurrent winner already refreshed while this request was in flight.
                session.SetSessionAsync(CreateTokens("already-new", "refresh-1")).GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return Ok();
        });

        using var response = await SendAsync(session, refresher, inner, "api/v1/patients/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        refresher.CallCount.Should().Be(0);
        session.Current.AccessToken.Should().Be("already-new");
        inner.SendCount.Should().Be(2);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        IAuthSessionService session,
        ITokenRefresher refresher,
        StubHandler inner,
        string path)
    {
        var handler = CreateHandler(session, refresher, inner);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        return await client.GetAsync(path);
    }

    private static AuthenticatingHttpMessageHandler CreateHandler(
        IAuthSessionService session,
        ITokenRefresher refresher,
        StubHandler inner) =>
        new(session, refresher, NullLogger<AuthenticatingHttpMessageHandler>.Instance)
        {
            InnerHandler = inner,
        };

    private static async Task<IAuthSessionService> CreateSessionAsync(string access, string refresh)
    {
        var service = new AuthSessionService(new InMemorySecureTokenStore(), NullLogger<AuthSessionService>.Instance);
        await service.SetSessionAsync(CreateTokens(access, refresh));
        return service;
    }

    private static AuthTokenResponse CreateTokens(string access, string refresh) =>
        new()
        {
            AccessToken = access,
            RefreshToken = refresh,
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        };

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new { ok = true }) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FakeTokenRefresher : ITokenRefresher
    {
        private readonly Func<string, Task<bool>> _impl;

        public FakeTokenRefresher(Func<string, Task<bool>> impl) => _impl = impl;

        public int CallCount { get; private set; }

        public async Task<bool> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return await _impl(refreshToken);
        }
    }
}
