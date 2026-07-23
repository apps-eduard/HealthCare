using System.Net.Http.Json;
using System.Security.Claims;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Services;

namespace HealthCare.Web.Auth;

/// <summary>
/// Server-to-server auth operations used by BFF endpoints. Never returns tokens to the browser.
/// Login is a single antiforgery-protected POST that creates a new session and issues the auth cookie
/// (no separate establish GET) — login CSRF is blocked by antiforgery binding to the victim browser.
/// </summary>
public interface IBffAuthService
{
    Task<BffLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    Task LogoutAsync(string? sessionId, bool callRemoteLogout, CancellationToken cancellationToken = default);
}

public sealed class BffLoginResult
{
    public bool Succeeded { get; init; }

    public string? ErrorCode { get; init; }

    public CurrentUserResponse? User { get; init; }

    public ApiTokenSession? Session { get; init; }

    public bool IsPatientOnly { get; init; }

    public bool IsStaffUser { get; init; }
}

public sealed class BffAuthService : IBffAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IApiTokenSessionStore _sessions;
    private readonly ILogger<BffAuthService> _logger;

    public BffAuthService(
        IHttpClientFactory httpClientFactory,
        IApiTokenSessionStore sessions,
        ILogger<BffAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task<BffLoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("BFF auth event. Event=login_initiated");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogInformation("BFF auth event. Event=login_failed ReasonCode=invalid_credentials");
            return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
        }

        var anonymous = _httpClientFactory.CreateClient("HealthCareApi.Anonymous");
        HttpResponseMessage loginResponse;
        try
        {
            loginResponse = await anonymous.PostAsJsonAsync(
                "api/v1/auth/login",
                new LoginRequest { Email = email.Trim(), Password = password },
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogInformation(ex, "BFF auth event. Event=login_failed ReasonCode=sign_in_failed");
            return new BffLoginResult { Succeeded = false, ErrorCode = "sign_in_failed" };
        }

        using (loginResponse)
        {
            if (!loginResponse.IsSuccessStatusCode)
            {
                var problem = await ApiProblemException.FromResponseAsync(loginResponse, cancellationToken);
                _logger.LogInformation(
                    "BFF auth event. Event=login_failed ReasonCode={ReasonCode}",
                    problem.ErrorCode ?? AuthErrorCodes.InvalidCredentials);
                return new BffLoginResult
                {
                    Succeeded = false,
                    ErrorCode = problem.ErrorCode ?? AuthErrorCodes.InvalidCredentials,
                };
            }

            var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
            {
                _logger.LogInformation("BFF auth event. Event=login_failed ReasonCode=invalid_credentials");
                return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
            }

            using var meRequest = new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/me");
            meRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            HttpResponseMessage meResponse;
            try
            {
                meResponse = await anonymous.SendAsync(meRequest, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogInformation(ex, "BFF auth event. Event=login_failed ReasonCode=sign_in_failed");
                return new BffLoginResult { Succeeded = false, ErrorCode = "sign_in_failed" };
            }

            using (meResponse)
            {
                if (!meResponse.IsSuccessStatusCode)
                {
                    _logger.LogInformation("BFF auth event. Event=login_failed ReasonCode=profile_unavailable");
                    return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
                }

                var user = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
                if (user is null)
                {
                    _logger.LogInformation("BFF auth event. Event=login_failed ReasonCode=invalid_credentials");
                    return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
                }

                // Always create a brand-new high-entropy session (session fixation defense).
                var session = await _sessions.CreateAsync(
                    user.UserId,
                    new StoredAuthTokens
                    {
                        AccessToken = tokens.AccessToken,
                        RefreshToken = tokens.RefreshToken,
                        AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
                        RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc,
                    },
                    cancellationToken);

                var isPlatform = user.Roles.Contains(WebRoles.PlatformAdmin, StringComparer.Ordinal);
                var isStaff = user.HasActiveStaffMembership || isPlatform;
                var isPatientOnly = user.Roles.Contains(WebRoles.Patient, StringComparer.Ordinal)
                                    && !user.HasActiveStaffMembership
                                    && !isPlatform;

                _logger.LogInformation(
                    "BFF auth event. Event=login_succeeded UserId={UserId} EventDetail=session_rotated",
                    user.UserId);

                return new BffLoginResult
                {
                    Succeeded = true,
                    User = user,
                    Session = session,
                    IsPatientOnly = isPatientOnly,
                    IsStaffUser = isStaff,
                };
            }
        }
    }

    public async Task LogoutAsync(
        string? sessionId,
        bool callRemoteLogout,
        CancellationToken cancellationToken = default)
    {
        string? refreshToken = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var session = await _sessions.GetAsync(sessionId, cancellationToken);
            refreshToken = session?.Tokens.RefreshToken;
            await _sessions.RemoveAsync(sessionId, cancellationToken);
        }

        if (callRemoteLogout && !string.IsNullOrWhiteSpace(refreshToken))
        {
            try
            {
                var client = _httpClientFactory.CreateClient("HealthCareApi.Anonymous");
                using var response = await client.PostAsJsonAsync(
                    "api/v1/auth/logout",
                    new LogoutRequest { RefreshToken = refreshToken },
                    cancellationToken);
                _ = response;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "BFF auth event. Event=logout_api_revocation_failed");
            }
        }

        _logger.LogInformation("BFF auth event. Event=logout_completed");
    }
}
