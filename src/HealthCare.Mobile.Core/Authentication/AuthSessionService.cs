using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Storage;
using Microsoft.Extensions.Logging;

namespace HealthCare.Mobile.Core.Authentication;

public sealed class AuthSessionService : IAuthSessionService
{
    private readonly ISecureTokenStore _store;
    private readonly ILogger<AuthSessionService> _logger;
    private readonly object _gate = new();
    private AuthSession _current = AuthSession.Anonymous;

    public AuthSessionService(ISecureTokenStore store, ILogger<AuthSessionService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public event Action? SessionChanged;

    public AuthSession Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public bool IsAuthenticated => Current.IsAuthenticated;

    public bool IsPatientReady => Current.IsPatientReady;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var access = await _store.GetAsync(SecureTokenKeys.AccessToken, cancellationToken);
            var refresh = await _store.GetAsync(SecureTokenKeys.RefreshToken, cancellationToken);
            var accessExp = ParseOffset(await _store.GetAsync(SecureTokenKeys.AccessTokenExpiresAtUtc, cancellationToken));
            var refreshExp = ParseOffset(await _store.GetAsync(SecureTokenKeys.RefreshTokenExpiresAtUtc, cancellationToken));

            if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) || accessExp is null)
            {
                await ClearSessionAsync(cancellationToken);
                return;
            }

            SetCurrent(new AuthSession
            {
                AccessToken = access,
                RefreshToken = refresh,
                AccessTokenExpiresAtUtc = accessExp,
                RefreshTokenExpiresAtUtc = refreshExp,
            });

            _logger.LogInformation("Auth session restored from secure storage. HasRefresh={HasRefresh}", !string.IsNullOrWhiteSpace(refresh));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore auth session; clearing secure storage.");
            await ClearSessionAsync(cancellationToken);
        }
    }

    public async Task SetSessionAsync(
        AuthTokenResponse tokens,
        CurrentUserResponse? user = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        await _store.SetAsync(SecureTokenKeys.AccessToken, tokens.AccessToken, cancellationToken);
        await _store.SetAsync(SecureTokenKeys.RefreshToken, tokens.RefreshToken, cancellationToken);
        await _store.SetAsync(
            SecureTokenKeys.AccessTokenExpiresAtUtc,
            tokens.AccessTokenExpiresAtUtc.UtcDateTime.ToString("O"),
            cancellationToken);
        await _store.SetAsync(
            SecureTokenKeys.RefreshTokenExpiresAtUtc,
            tokens.RefreshTokenExpiresAtUtc.UtcDateTime.ToString("O"),
            cancellationToken);

        SetCurrent(AuthSession.FromTokens(tokens, user));
        _logger.LogInformation("Auth session established. LinkedPatient={Linked}", user?.HasLinkedPatient == true);
    }

    public Task UpdateCurrentUserAsync(CurrentUserResponse user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var existing = Current;
        SetCurrent(new AuthSession
        {
            AccessToken = existing.AccessToken,
            RefreshToken = existing.RefreshToken,
            AccessTokenExpiresAtUtc = existing.AccessTokenExpiresAtUtc,
            RefreshTokenExpiresAtUtc = existing.RefreshTokenExpiresAtUtc,
            CurrentUser = user,
        });
        return Task.CompletedTask;
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        await _store.ClearSessionAsync(cancellationToken);
        SetCurrent(AuthSession.Anonymous);
        _logger.LogInformation("Auth session cleared.");
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var session = Current;
        if (!session.IsAuthenticated)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(session.AccessToken);
    }

    private void SetCurrent(AuthSession session)
    {
        lock (_gate)
        {
            _current = session;
        }

        SessionChanged?.Invoke();
    }

    private static DateTimeOffset? ParseOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
