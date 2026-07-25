using HealthCare.Contracts.Identity;

namespace HealthCare.Mobile.Core.Authentication;

public sealed class AuthSession
{
    public string? AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public DateTimeOffset? AccessTokenExpiresAtUtc { get; init; }

    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; init; }

    public CurrentUserResponse? CurrentUser { get; init; }

    /// <summary>
    /// Session present (tokens restored). Access may be near expiry; the API handler refreshes on 401.
    /// Do not treat JWT claims as authoritative Patient identity — call the API.
    /// </summary>
    public bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(AccessToken) && HasRefreshToken;

    public bool IsAccessTokenExpiredOrExpiring =>
        AccessTokenExpiresAtUtc is null
        || AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddSeconds(30);

    public bool HasRefreshToken => !string.IsNullOrWhiteSpace(RefreshToken);

    public bool HasLinkedPatient => CurrentUser?.HasLinkedPatient == true && CurrentUser.PatientId is not null;

    public static AuthSession Anonymous { get; } = new();

    public static AuthSession FromTokens(AuthTokenResponse tokens, CurrentUserResponse? user = null) =>
        new()
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
            RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc,
            CurrentUser = user,
        };
}

public interface IAuthSessionService
{
    event Action? SessionChanged;

    AuthSession Current { get; }

    bool IsAuthenticated { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task SetSessionAsync(AuthTokenResponse tokens, CurrentUserResponse? user = null, CancellationToken cancellationToken = default);

    Task UpdateCurrentUserAsync(CurrentUserResponse user, CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);

    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
