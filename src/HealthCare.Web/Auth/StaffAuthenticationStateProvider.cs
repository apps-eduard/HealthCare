using System.Net.Http.Json;
using System.Security.Claims;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace HealthCare.Web.Auth;

/// <summary>
/// Authentication state is derived from the HttpOnly Web cookie + server-side BFF token session.
/// Browser storage is never consulted for API tokens.
/// </summary>
public sealed class StaffAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IApiTokenStore _tokenStore;
    private readonly IApiTokenSessionStore _sessions;
    private readonly IPermissionState _permissionState;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClinicDirectoryCache _clinicCache;
    private readonly IPlatformTenantContext _platformTenant;
    private readonly IClinicWorkingContext _clinicWorking;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<StaffAuthenticationStateProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Guid? _authenticatedUserId;

    public StaffAuthenticationStateProvider(
        IApiTokenStore tokenStore,
        IApiTokenSessionStore sessions,
        IPermissionState permissionState,
        IHttpClientFactory httpClientFactory,
        IClinicDirectoryCache clinicCache,
        IPlatformTenantContext platformTenant,
        IClinicWorkingContext clinicWorking,
        IHttpContextAccessor httpContextAccessor,
        ILogger<StaffAuthenticationStateProvider> logger)
    {
        _tokenStore = tokenStore;
        _sessions = sessions;
        _permissionState = permissionState;
        _httpClientFactory = httpClientFactory;
        _clinicCache = clinicCache;
        _platformTenant = platformTenant;
        _clinicWorking = clinicWorking;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var sessionId = ResolveSessionId();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                await ClearLocalCircuitStateAsync();
                return Anonymous();
            }

            var session = await _sessions.GetAsync(sessionId);
            if (session is null)
            {
                await ClearLocalCircuitStateAsync();
                return Anonymous();
            }

            var cookieUserId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(cookieUserId, out var principalUserId)
                && principalUserId != Guid.Empty
                && principalUserId != session.UserId)
            {
                _logger.LogInformation("BFF auth event. Event=session_mismatch");
                await _sessions.RemoveAsync(sessionId);
                await ClearLocalCircuitStateAsync();
                return Anonymous();
            }

            if (_permissionState.CurrentUser is null || !_permissionState.IsReady
                || _authenticatedUserId != session.UserId)
            {
                var loaded = await TryLoadCurrentUserAsync();
                if (!loaded)
                {
                    await _sessions.RemoveAsync(sessionId);
                    await ClearLocalCircuitStateAsync();
                    return Anonymous();
                }
            }

            return Authenticated(_permissionState.CurrentUser!);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Clears circuit permission/tenant state and server token session. Cookie is cleared via BFF logout redirect.
    /// </summary>
    public async Task MarkLoggedOutAsync(bool callRemoteLogout = true)
    {
        var sessionId = ResolveSessionId();
        string? refreshToken = null;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var session = await _sessions.GetAsync(sessionId);
            refreshToken = session?.Tokens.RefreshToken;
            await _sessions.RemoveAsync(sessionId);
        }

        if (callRemoteLogout && !string.IsNullOrWhiteSpace(refreshToken))
        {
            try
            {
                var client = _httpClientFactory.CreateClient("HealthCareApi.Anonymous");
                using var response = await client.PostAsJsonAsync(
                    "api/v1/auth/logout",
                    new LogoutRequest { RefreshToken = refreshToken });
                _ = response;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Remote logout failed; clearing local session anyway.");
            }
        }

        await ClearLocalCircuitStateAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous()));
    }

    public async Task RefreshProfileAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await TryLoadCurrentUserAsync(cancellationToken);
        if (!loaded)
        {
            await MarkLoggedOutAsync(callRemoteLogout: false);
            return;
        }

        NotifyAuthenticationStateChanged(Task.FromResult(Authenticated(_permissionState.CurrentUser!)));
    }

    private string? ResolveSessionId()
    {
        var fromStore = _tokenStore.GetCurrentSessionId();
        if (!string.IsNullOrWhiteSpace(fromStore))
        {
            return fromStore;
        }

        return _httpContextAccessor.HttpContext?.User?.FindFirstValue(BffClaimTypes.SessionId);
    }

    private async Task ClearLocalCircuitStateAsync()
    {
        await _permissionState.ClearAsync();
        _clinicCache.Clear();
        _platformTenant.Clear();
        _clinicWorking.Clear();
        _authenticatedUserId = null;
    }

    private async Task<bool> TryLoadCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("HealthCareApi");
            using var response = await client.GetAsync("api/v1/auth/me", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var user = await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken);
            if (user is null)
            {
                return false;
            }

            if (_authenticatedUserId is Guid previous && previous != user.UserId)
            {
                _platformTenant.Clear();
                _clinicWorking.Clear();
                _clinicCache.Clear();
            }

            _authenticatedUserId = user.UserId;
            await _permissionState.SetFromUserAsync(user, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Failed to load current user profile.");
            return false;
        }
    }

    private static AuthenticationState Anonymous() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static AuthenticationState Authenticated(CurrentUserResponse user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString("D")),
            new(ClaimTypes.Name, user.Email ?? user.UserId.ToString("D")),
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in user.Permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var identity = new ClaimsIdentity(claims, authenticationType: StaffWebAuthCookie.AuthenticationType);
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
