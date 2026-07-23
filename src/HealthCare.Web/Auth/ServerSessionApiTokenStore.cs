using System.Security.Claims;

namespace HealthCare.Web.Auth;

/// <summary>
/// Resolves the current BFF session id and delegates token get/set/clear to the server session store.
/// Never reads or writes browser storage.
/// </summary>
public sealed class ServerSessionApiTokenStore : IApiTokenStore
{
    private readonly IApiTokenSessionStore _sessions;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ServerSessionApiTokenStore> _logger;

    public ServerSessionApiTokenStore(
        IApiTokenSessionStore sessions,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ServerSessionApiTokenStore> logger)
    {
        _sessions = sessions;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public string? GetCurrentSessionId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var sid = user.FindFirstValue(BffClaimTypes.SessionId);
        return string.IsNullOrWhiteSpace(sid) ? null : sid;
    }

    public async Task<StoredAuthTokens?> GetAsync(CancellationToken cancellationToken = default)
    {
        var withVersion = await GetWithVersionAsync(cancellationToken);
        return withVersion?.Tokens;
    }

    public async Task<(StoredAuthTokens Tokens, long Version)?> GetWithVersionAsync(
        CancellationToken cancellationToken = default)
    {
        var sessionId = GetCurrentSessionId();
        if (sessionId is null)
        {
            return null;
        }

        var session = await _sessions.GetAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        await _sessions.TouchAsync(sessionId, cancellationToken);
        return (session.Tokens, session.Version);
    }

    public async Task<bool> TrySetAsync(
        StoredAuthTokens tokens,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var sessionId = GetCurrentSessionId();
        if (sessionId is null)
        {
            return false;
        }

        var updated = await _sessions.TryUpdateTokensAsync(sessionId, expectedVersion, tokens, cancellationToken);
        if (!updated)
        {
            _logger.LogInformation(
                "BFF token refresh suppressed because session was revoked or version mismatch. Event=refresh_suppressed");
        }

        return updated;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = GetCurrentSessionId();
        if (sessionId is null)
        {
            return;
        }

        await _sessions.RemoveAsync(sessionId, cancellationToken);
        _logger.LogInformation("Cleared BFF token session. Event=session_cleared");
    }
}
