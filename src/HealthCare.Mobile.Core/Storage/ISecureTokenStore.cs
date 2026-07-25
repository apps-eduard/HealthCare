namespace HealthCare.Mobile.Core.Storage;

/// <summary>
/// Secure token persistence. Implementations must use platform secure storage (e.g. MAUI SecureStorage).
/// Never fall back to plain preferences for tokens.
/// </summary>
public interface ISecureTokenStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}

public static class SecureTokenKeys
{
    public const string AccessToken = "healthcare.patient.access_token";
    public const string RefreshToken = "healthcare.patient.refresh_token";
    public const string AccessTokenExpiresAtUtc = "healthcare.patient.access_expires_utc";
    public const string RefreshTokenExpiresAtUtc = "healthcare.patient.refresh_expires_utc";
}
