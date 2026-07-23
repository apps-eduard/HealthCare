using System.Net.Http.Json;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Services;

namespace HealthCare.Web.Auth;

/// <summary>
/// Server-to-server auth operations used by BFF endpoints. Never returns tokens to the browser.
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

    public string? EstablishTicket { get; init; }

    public Guid? UserId { get; init; }

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
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
        }

        var anonymous = _httpClientFactory.CreateClient("HealthCareApi.Anonymous");
        using var loginResponse = await anonymous.PostAsJsonAsync(
            "api/v1/auth/login",
            new LoginRequest { Email = email.Trim(), Password = password },
            cancellationToken);

        if (!loginResponse.IsSuccessStatusCode)
        {
            var problem = await ApiProblemException.FromResponseAsync(loginResponse, cancellationToken);
            return new BffLoginResult
            {
                Succeeded = false,
                ErrorCode = problem.ErrorCode ?? AuthErrorCodes.InvalidCredentials,
            };
        }

        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
        }

        // Load /me with the new access token (do not use circuit token store — no cookie yet).
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/me");
        meRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var meResponse = await anonymous.SendAsync(meRequest, cancellationToken);
        if (!meResponse.IsSuccessStatusCode)
        {
            _logger.LogInformation("BFF login succeeded but /me failed with {StatusCode}.", (int)meResponse.StatusCode);
            return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
        }

        var user = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
        if (user is null)
        {
            return new BffLoginResult { Succeeded = false, ErrorCode = AuthErrorCodes.InvalidCredentials };
        }

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

        var ticket = await _sessions.CreateLoginTicketAsync(session.SessionId, user.UserId, cancellationToken);

        var isPlatform = user.Roles.Contains(WebRoles.PlatformAdmin, StringComparer.Ordinal);
        var isStaff = user.HasActiveStaffMembership || isPlatform;
        var isPatientOnly = user.Roles.Contains(WebRoles.Patient, StringComparer.Ordinal)
                            && !user.HasActiveStaffMembership
                            && !isPlatform;

        _logger.LogInformation("BFF login ticket created for user {UserId}.", user.UserId);

        return new BffLoginResult
        {
            Succeeded = true,
            EstablishTicket = ticket,
            UserId = user.UserId,
            IsPatientOnly = isPatientOnly,
            IsStaffUser = isStaff,
        };
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
                _logger.LogInformation(ex, "Remote API logout failed; local BFF session already cleared.");
            }
        }
    }
}
