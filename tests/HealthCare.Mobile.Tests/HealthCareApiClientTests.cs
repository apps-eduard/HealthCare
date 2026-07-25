using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class HealthCareApiClientTests
{
    [Fact]
    public async Task GetHealthAsync_Accepts_PlainText_Healthy()
    {
        var handler = new QueueHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Healthy"),
        });

        var (client, _) = Create(handler);
        var result = await client.GetHealthAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be("Healthy");
    }

    [Fact]
    public async Task LoginAsync_Stores_Session_On_Success()
    {
        var handler = new QueueHandler();
        handler.Enqueue(Json(HttpStatusCode.OK, new AuthTokenResponse
        {
            AccessToken = "a",
            RefreshToken = "r",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        }));

        var (client, session) = Create(handler);
        var result = await client.LoginAsync(new LoginRequest { Email = "a@b.c", Password = "secret" });

        result.IsSuccess.Should().BeTrue();
        session.IsAuthenticated.Should().BeTrue();
        session.Current.AccessToken.Should().Be("a");
    }

    [Fact]
    public async Task GetMeAsync_Maps_403_Without_Leaking_Body()
    {
        var handler = new QueueHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"title":"Forbidden","detail":"internal stack TRACE sql"}"""),
        });

        var (client, session) = Create(handler);
        await session.SetSessionAsync(new AuthTokenResponse
        {
            AccessToken = "a",
            RefreshToken = "r",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        });

        var result = await client.GetMeAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ApiErrorKind.Forbidden);
        result.Error.UserMessage.Should().NotContain("TRACE");
        result.Error.UserMessage.Should().NotContain("sql");
    }

    [Fact]
    public async Task GetHealthAsync_Maps_Network_Failure()
    {
        var (client, _) = Create(new ThrowingHandler(new HttpRequestException("offline")));
        var result = await client.GetHealthAsync();

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ApiErrorKind.Network);
    }

    [Fact]
    public async Task GetMeAsync_Maps_409_Conflict()
    {
        var handler = new QueueHandler();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("""{"title":"Conflict","detail":"Slot taken","errorCode":"appointment.slot_conflict"}"""),
        });

        var (client, session) = Create(handler);
        await session.SetSessionAsync(new AuthTokenResponse
        {
            AccessToken = "a",
            RefreshToken = "r",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        });

        var result = await client.GetMeAsync();
        result.Error!.Kind.Should().Be(ApiErrorKind.Conflict);
        result.Error.ErrorCode.Should().Be("appointment.slot_conflict");
    }

    private static (IHealthCareApiClient Client, IAuthSessionService Session) Create(HttpMessageHandler handler)
    {
        var session = new AuthSessionService(new InMemorySecureTokenStore(), NullLogger<AuthSessionService>.Instance);
        var factory = new NamedHttpClientFactory(handler);
        var client = new HealthCareApiClient(factory, session, NullLogger<HealthCareApiClient>.Instance);
        return (client, session);
    }

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T body) =>
        new(status) { Content = JsonContent.Create(body) };

    private sealed class NamedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public NamedHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responses.Dequeue());
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(_exception);
    }
}
