namespace HealthCare.Web.Auth;

public static class BffClaimTypes
{
    /// <summary>Opaque server token-session identifier. Never an API token.</summary>
    public const string SessionId = "bff_sid";
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

    Task UpdateTokensAsync(string sessionId, StoredAuthTokens tokens, CancellationToken cancellationToken = default);

    Task TouchAsync(string sessionId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<string> CreateLoginTicketAsync(string sessionId, Guid userId, CancellationToken cancellationToken = default);

    Task<(string SessionId, Guid UserId)?> ConsumeLoginTicketAsync(string ticket, CancellationToken cancellationToken = default);

    /// <summary>Per-session refresh serialization (single-instance safe; production multi-node may add distributed locks later).</summary>
    SemaphoreSlim GetRefreshLock(string sessionId);
}

/// <summary>
/// Circuit/request-scoped accessor for the current session's API tokens.
/// </summary>
public interface IApiTokenStore
{
    Task<StoredAuthTokens?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(StoredAuthTokens tokens, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    string? GetCurrentSessionId();
}
