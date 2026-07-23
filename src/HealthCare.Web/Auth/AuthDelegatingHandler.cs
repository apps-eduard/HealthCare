using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HealthCare.Contracts.Identity;
using Microsoft.Extensions.Logging;

namespace HealthCare.Web.Auth;

/// <summary>
/// Adds bearer access token from the server BFF session and performs a single coordinated refresh on 401.
/// Never logs Authorization headers or token values. Never exposes tokens to the browser.
/// </summary>
public sealed class AuthDelegatingHandler : DelegatingHandler
{
    private readonly IApiTokenStore _tokenStore;
    private readonly IApiTokenSessionStore _sessions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _services;
    private readonly ILogger<AuthDelegatingHandler> _logger;

    public AuthDelegatingHandler(
        IApiTokenStore tokenStore,
        IApiTokenSessionStore sessions,
        IHttpClientFactory httpClientFactory,
        IServiceProvider services,
        ILogger<AuthDelegatingHandler> logger)
    {
        _tokenStore = tokenStore;
        _sessions = sessions;
        _httpClientFactory = httpClientFactory;
        _services = services;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(SkipAuthOption, out var skip) && skip)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        await AttachAccessTokenAsync(request, cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var refreshed = await TryRefreshAsync(cancellationToken);
        if (!refreshed)
        {
            await ForceLocalLogoutAsync();
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                RequestMessage = request,
            };
        }

        var retry = await CloneRequestAsync(request, cancellationToken);
        await AttachAccessTokenAsync(retry, cancellationToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    public static readonly HttpRequestOptionsKey<bool> SkipAuthOption = new("HealthCare.SkipAuth");

    private async Task AttachAccessTokenAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var tokens = await _tokenStore.GetAsync(cancellationToken);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        var sessionId = _tokenStore.GetCurrentSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        var gate = _sessions.GetRefreshLock(sessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check session after acquiring the lock (logout may have won the race).
            var current = await _tokenStore.GetWithVersionAsync(cancellationToken);
            if (current is null || string.IsNullOrWhiteSpace(current.Value.Tokens.RefreshToken))
            {
                _logger.LogInformation("BFF auth event. Event=refresh_suppressed ReasonCode=session_missing");
                return false;
            }

            var (tokens, version) = current.Value;
            if (tokens.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                return true;
            }

            var client = _httpClientFactory.CreateClient("HealthCareApi.Anonymous");
            using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/refresh")
            {
                Content = JsonContent.Create(new RefreshTokenRequest { RefreshToken = tokens.RefreshToken }),
            };
            refreshRequest.Options.Set(SkipAuthOption, true);

            using var refreshResponse = await client.SendAsync(refreshRequest, cancellationToken);
            if (!refreshResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Access token refresh failed with status {StatusCode}.", (int)refreshResponse.StatusCode);
                return false;
            }

            var body = await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
            if (body is null || string.IsNullOrWhiteSpace(body.AccessToken))
            {
                return false;
            }

            // Final CAS write — must not recreate a logged-out session.
            var stored = await _tokenStore.TrySetAsync(
                new StoredAuthTokens
                {
                    AccessToken = body.AccessToken,
                    RefreshToken = body.RefreshToken,
                    AccessTokenExpiresAtUtc = body.AccessTokenExpiresAtUtc,
                    RefreshTokenExpiresAtUtc = body.RefreshTokenExpiresAtUtc,
                },
                version,
                cancellationToken);

            if (!stored)
            {
                _logger.LogInformation("BFF auth event. Event=refresh_suppressed ReasonCode=session_revoked");
            }

            return stored;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Access token refresh failed.");
            return false;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ForceLocalLogoutAsync()
    {
        try
        {
            var authState = _services.GetService<StaffAuthenticationStateProvider>();
            if (authState is not null)
            {
                await authState.MarkLoggedOutAsync(callRemoteLogout: false);
            }
            else
            {
                await _tokenStore.ClearAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Local logout after refresh failure encountered an error.");
            await _tokenStore.ClearAsync();
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
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
