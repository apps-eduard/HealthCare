using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using HealthCare.Web.Configuration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace HealthCare.Web.Auth;

/// <summary>
/// Distributed-cache backed token sessions. Values are data-protected (encrypted) at rest in cache.
/// Development typically uses AddDistributedMemoryCache; production requires a shared cache for multi-instance.
/// </summary>
public sealed class DistributedCacheApiTokenSessionStore : IApiTokenSessionStore
{
    private const string SessionKeyPrefix = "bff:session:";
    private const string TicketKeyPrefix = "bff:ticket:";
    private const string ProtectorPurpose = "HealthCare.Web.Bff.TokenSession.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;
    private readonly BffOptions _options;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.Ordinal);

    public DistributedCacheApiTokenSessionStore(
        IDistributedCache cache,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<BffOptions> options)
    {
        _cache = cache;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _options = options.Value;
    }

    public SemaphoreSlim GetRefreshLock(string sessionId) =>
        _refreshLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

    public async Task<ApiTokenSession> CreateAsync(
        Guid userId,
        StoredAuthTokens tokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        var now = DateTimeOffset.UtcNow;
        var absolute = now.AddHours(Math.Max(1, _options.AbsoluteSessionHours));
        var refreshCap = tokens.RefreshTokenExpiresAtUtc;
        if (refreshCap < absolute)
        {
            absolute = refreshCap;
        }

        var session = new ApiTokenSession
        {
            SessionId = CreateOpaqueId(),
            UserId = userId,
            Tokens = tokens,
            CreatedAtUtc = now,
            AbsoluteExpiresAtUtc = absolute,
            LastAccessedAtUtc = now,
        };

        await SaveAsync(session, cancellationToken);
        return session;
    }

    public async Task<ApiTokenSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var session = await ReadAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (session.AbsoluteExpiresAtUtc <= now)
        {
            await RemoveAsync(sessionId, cancellationToken);
            return null;
        }

        var idle = TimeSpan.FromMinutes(Math.Max(1, _options.SessionIdleMinutes));
        if (session.LastAccessedAtUtc + idle <= now)
        {
            await RemoveAsync(sessionId, cancellationToken);
            return null;
        }

        return session;
    }

    public async Task<bool> ExistsAsync(string sessionId, CancellationToken cancellationToken = default) =>
        await GetAsync(sessionId, cancellationToken) is not null;

    public async Task UpdateTokensAsync(
        string sessionId,
        StoredAuthTokens tokens,
        CancellationToken cancellationToken = default)
    {
        var session = await GetAsync(sessionId, cancellationToken)
                      ?? throw new InvalidOperationException("Token session was not found.");

        session.Tokens = tokens;
        session.LastAccessedAtUtc = DateTimeOffset.UtcNow;
        if (tokens.RefreshTokenExpiresAtUtc < session.AbsoluteExpiresAtUtc)
        {
            session.AbsoluteExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc;
        }

        await SaveAsync(session, cancellationToken);
    }

    public async Task TouchAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.LastAccessedAtUtc = DateTimeOffset.UtcNow;
        await SaveAsync(session, cancellationToken);
    }

    public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _cache.RemoveAsync(SessionKeyPrefix + sessionId, cancellationToken);
        if (_refreshLocks.TryRemove(sessionId, out var gate))
        {
            gate.Dispose();
        }
    }

    public async Task<string> CreateLoginTicketAsync(
        string sessionId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var ticket = CreateOpaqueId();
        var payload = _protector.Protect(JsonSerializer.Serialize(new TicketPayload(sessionId, userId), JsonOptions));
        var seconds = Math.Clamp(_options.LoginTicketSeconds, 15, 300);
        await _cache.SetStringAsync(
            TicketKeyPrefix + ticket,
            payload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(seconds),
            },
            cancellationToken);
        return ticket;
    }

    public async Task<(string SessionId, Guid UserId)?> ConsumeLoginTicketAsync(
        string ticket,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return null;
        }

        var key = TicketKeyPrefix + ticket;
        var protectedPayload = await _cache.GetStringAsync(key, cancellationToken);
        await _cache.RemoveAsync(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return null;
        }

        try
        {
            var json = _protector.Unprotect(protectedPayload);
            var payload = JsonSerializer.Deserialize<TicketPayload>(json, JsonOptions);
            if (payload is null || string.IsNullOrWhiteSpace(payload.SessionId) || payload.UserId == Guid.Empty)
            {
                return null;
            }

            return (payload.SessionId, payload.UserId);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private async Task SaveAsync(ApiTokenSession session, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(session, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        var ttl = session.AbsoluteExpiresAtUtc - DateTimeOffset.UtcNow;
        if (ttl < TimeSpan.FromSeconds(30))
        {
            ttl = TimeSpan.FromSeconds(30);
        }

        var idle = TimeSpan.FromMinutes(Math.Max(1, _options.SessionIdleMinutes));
        await _cache.SetStringAsync(
            SessionKeyPrefix + session.SessionId,
            protectedPayload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session.AbsoluteExpiresAtUtc,
                SlidingExpiration = idle < ttl ? idle : null,
            },
            cancellationToken);
    }

    private async Task<ApiTokenSession?> ReadAsync(string sessionId, CancellationToken cancellationToken)
    {
        var protectedPayload = await _cache.GetStringAsync(SessionKeyPrefix + sessionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(protectedPayload))
        {
            return null;
        }

        try
        {
            var json = _protector.Unprotect(protectedPayload);
            return JsonSerializer.Deserialize<ApiTokenSession>(json, JsonOptions);
        }
        catch (CryptographicException)
        {
            await _cache.RemoveAsync(SessionKeyPrefix + sessionId, cancellationToken);
            return null;
        }
    }

    private static string CreateOpaqueId()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record TicketPayload(string SessionId, Guid UserId);
}
