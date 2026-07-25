using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Authentication;
using Microsoft.Extensions.Logging;

namespace HealthCare.Mobile.Core.Api;

/// <summary>
/// Attaches the bearer access token and performs at most one refresh+retry on 401.
/// </summary>
public sealed class AuthenticatingHttpMessageHandler : DelegatingHandler
{
    private readonly IAuthSessionService _session;
    private readonly ITokenRefresher _refresher;
    private readonly ILogger<AuthenticatingHttpMessageHandler> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthenticatingHttpMessageHandler(
        IAuthSessionService session,
        ITokenRefresher refresher,
        ILogger<AuthenticatingHttpMessageHandler> logger)
    {
        _session = session;
        _refresher = refresher;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await AttachAccessTokenAsync(request, cancellationToken);
        var accessBefore = request.Headers.Authorization?.Parameter;

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || IsAuthEndpoint(request.RequestUri))
        {
            return response;
        }

        response.Dispose();

        var refreshed = await TryRefreshOnceAsync(accessBefore, cancellationToken);
        if (!refreshed)
        {
            await _session.ClearSessionAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = request,
                ReasonPhrase = "Session expired",
            };
        }

        // Retry the original request once with the new token.
        using var retry = await CloneAsync(request, cancellationToken);
        await AttachAccessTokenAsync(retry, cancellationToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private async Task AttachAccessTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _session.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task<bool> TryRefreshOnceAsync(string? accessTokenAtUnauthorized, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Another concurrent 401 may have already refreshed.
            var currentAccess = _session.Current.AccessToken;
            if (!string.IsNullOrWhiteSpace(currentAccess)
                && !string.Equals(currentAccess, accessTokenAtUnauthorized, StringComparison.Ordinal)
                && _session.Current.HasRefreshToken)
            {
                return true;
            }

            var refresh = _session.Current.RefreshToken;
            if (string.IsNullOrWhiteSpace(refresh))
            {
                return false;
            }

            _logger.LogInformation("Attempting access-token refresh after 401.");
            return await _refresher.TryRefreshAsync(refresh, cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static bool IsAuthEndpoint(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.Contains("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/api/v1/auth/register", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}

public interface ITokenRefresher
{
    Task<bool> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses a dedicated HttpClient (no auth handler) to refresh tokens and update the session.
/// </summary>
public sealed class TokenRefresher : ITokenRefresher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthSessionService _session;
    private readonly ILogger<TokenRefresher> _logger;

    public TokenRefresher(
        IHttpClientFactory httpClientFactory,
        IAuthSessionService session,
        ILogger<TokenRefresher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _session = session;
        _logger = logger;
    }

    public async Task<bool> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(MobileHttpClientNames.Anonymous);
            using var response = await client.PostAsJsonAsync(
                "api/v1/auth/refresh",
                new RefreshTokenRequest { RefreshToken = refreshToken },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Token refresh failed with status {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken: cancellationToken);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
            {
                return false;
            }

            await _session.SetSessionAsync(tokens, _session.Current.CurrentUser, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Token refresh network failure.");
            return false;
        }
    }
}

public static class MobileHttpClientNames
{
    public const string Authenticated = "HealthCare.Api.Authenticated";
    public const string Anonymous = "HealthCare.Api.Anonymous";
}
