namespace HealthCare.Web.Auth;

public static class BffClaimTypes
{
    /// <summary>Opaque server token-session identifier. Never an API token.</summary>
    public const string SessionId = "bff_sid";
}

public static class BffCookieNames
{
    /// <summary>Development / HTTP-compatible auth cookie name.</summary>
    public const string AuthDevelopment = "HealthCare.Staff.Auth";

    /// <summary>Production __Host- auth cookie (Secure, Path=/, no Domain).</summary>
    public const string AuthProduction = "__Host-HealthCare.Staff";

    /// <summary>Legacy login-ticket cookie name (deleted on logout for cleanup).</summary>
    public const string LegacyLoginTicket = "HealthCare.Staff.LoginTicket";

    /// <summary>Legacy correlation cookie name (deleted on logout for cleanup).</summary>
    public const string LegacyLoginCorrelation = "HealthCare.Staff.LoginCorrelation";
}

public sealed class StoredAuthTokens
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public required DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
}

public sealed class ApiTokenSession
{
    public required string SessionId { get; init; }

    public required Guid UserId { get; init; }

    public required StoredAuthTokens Tokens { get; set; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset AbsoluteExpiresAtUtc { get; set; }

    public DateTimeOffset LastAccessedAtUtc { get; set; }

    /// <summary>Monotonic version for compare-and-swap updates (logout/refresh race safety).</summary>
    public long Version { get; set; }
}

/// <summary>
/// Server-side API token session store. Browser never sees access/refresh tokens.
/// </summary>
public interface IApiTokenSessionStore
{
    Task<ApiTokenSession> CreateAsync(
        Guid userId,
        StoredAuthTokens tokens,
        CancellationToken cancellationToken = default);

    Task<ApiTokenSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates tokens only if the session still exists at the expected version.
    /// Returns false when the session was removed (e.g. logout) — never recreates a session.
    /// </summary>
    Task<bool> TryUpdateTokensAsync(
        string sessionId,
        long expectedVersion,
        StoredAuthTokens tokens,
        CancellationToken cancellationToken = default);

    Task TouchAsync(string sessionId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Per-session refresh serialization (process-local; multi-instance needs distributed locks).</summary>
    SemaphoreSlim GetRefreshLock(string sessionId);
}

/// <summary>
/// Circuit/request-scoped accessor for the current session's API tokens.
/// </summary>
public interface IApiTokenStore
{
    Task<StoredAuthTokens?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to store refreshed tokens. Returns false if the BFF session no longer exists.
    /// </summary>
    Task<bool> TrySetAsync(StoredAuthTokens tokens, long expectedVersion, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    string? GetCurrentSessionId();

    Task<(StoredAuthTokens Tokens, long Version)?> GetWithVersionAsync(CancellationToken cancellationToken = default);
}
