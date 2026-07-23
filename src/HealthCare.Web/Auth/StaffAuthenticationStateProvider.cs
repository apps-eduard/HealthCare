using System.Net.Http.Json;
using System.Security.Claims;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace HealthCare.Web.Auth;

public sealed class StaffAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IApiTokenStore _tokenStore;
    private readonly IPermissionState _permissionState;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClinicDirectoryCache _clinicCache;
    private readonly IStaffWebAuthCookie _webAuthCookie;
    private readonly ILogger<StaffAuthenticationStateProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StaffAuthenticationStateProvider(
        IApiTokenStore tokenStore,
        IPermissionState permissionState,
        IHttpClientFactory httpClientFactory,
        IClinicDirectoryCache clinicCache,
        IStaffWebAuthCookie webAuthCookie,
        ILogger<StaffAuthenticationStateProvider> logger)
    {
        _tokenStore = tokenStore;
        _permissionState = permissionState;
        _httpClientFactory = httpClientFactory;
        _clinicCache = clinicCache;
        _webAuthCookie = webAuthCookie;
        _logger = logger;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var tokens = await _tokenStore.GetAsync();
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
            {
                await _permissionState.ClearAsync();
                await ClearStaleWebCookieAsync();
                return Anonymous();
            }

            if (_permissionState.CurrentUser is null || !_permissionState.IsReady)
            {
                var loaded = await TryLoadCurrentUserAsync();
                if (!loaded)
                {
                    await _tokenStore.ClearAsync();
                    await _permissionState.ClearAsync();
                    await _webAuthCookie.SignOutAsync();
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

    private async Task ClearStaleWebCookieAsync()
    {
        // Only clear when a cookie principal is present to avoid unnecessary SignOut noise.
        // IHttpContextAccessor is used inside StaffWebAuthCookie; SignOut is safe when no cookie.
        await _webAuthCookie.SignOutAsync();
    }

    public async Task SignInAsync(AuthTokenResponse tokens, CancellationToken cancellationToken = default)
    {
        await _tokenStore.SetAsync(
            new StoredAuthTokens
            {
                AccessToken = tokens.AccessToken,
                RefreshToken = tokens.RefreshToken,
                AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
                RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc,
            },
            cancellationToken);

        var loaded = await TryLoadCurrentUserAsync(cancellationToken);
        if (!loaded)
        {
            await _tokenStore.ClearAsync(cancellationToken);
            await _permissionState.ClearAsync(cancellationToken);
            await _webAuthCookie.SignOutAsync(cancellationToken);
            throw new InvalidOperationException("Unable to load the current user after login.");
        }

        await _webAuthCookie.SignInAsync(_permissionState.CurrentUser!, cancellationToken);
        NotifyAuthenticationStateChanged(Task.FromResult(Authenticated(_permissionState.CurrentUser!)));
    }

    public async Task MarkLoggedOutAsync(bool callRemoteLogout = true)
    {
        if (callRemoteLogout)
        {
            try
            {
                var tokens = await _tokenStore.GetAsync();
                if (tokens is not null && !string.IsNullOrWhiteSpace(tokens.RefreshToken))
                {
                    var client = _httpClientFactory.CreateClient("HealthCareApi.Anonymous");
                    using var response = await client.PostAsJsonAsync(
                        "api/v1/auth/logout",
                        new LogoutRequest { RefreshToken = tokens.RefreshToken });
                    // Local clear proceeds regardless of remote result.
                    _ = response;
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Remote logout failed; clearing local session anyway.");
            }
        }

        await _tokenStore.ClearAsync();
        await _permissionState.ClearAsync();
        _clinicCache.Clear();
        await _webAuthCookie.SignOutAsync();
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

        await _webAuthCookie.SignInAsync(_permissionState.CurrentUser!, cancellationToken);
        NotifyAuthenticationStateChanged(Task.FromResult(Authenticated(_permissionState.CurrentUser!)));
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
