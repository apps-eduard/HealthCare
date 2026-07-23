using System.Text.Json;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace HealthCare.Web.Auth;

/// <summary>
/// Circuit-scoped token store with ProtectedSessionStorage persistence.
/// MVP limitation: refresh tokens are not HttpOnly cookies; a BFF cookie pattern is preferred later.
/// Tokens are never exposed to page markup and are never logged.
/// </summary>
public sealed class ProtectedSessionApiTokenStore : IApiTokenStore
{
    private const string StorageKey = "healthcare.staff.auth.tokens";

    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly ILogger<ProtectedSessionApiTokenStore> _logger;
    private StoredAuthTokens? _memory;
    private bool _loaded;

    public ProtectedSessionApiTokenStore(
        ProtectedSessionStorage sessionStorage,
        ILogger<ProtectedSessionApiTokenStore> logger)
    {
        _sessionStorage = sessionStorage;
        _logger = logger;
    }

    public async Task<StoredAuthTokens?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_memory is not null)
        {
            return _memory;
        }

        if (_loaded)
        {
            return null;
        }

        try
        {
            var result = await _sessionStorage.GetAsync<string>(StorageKey);
            _loaded = true;
            if (!result.Success || string.IsNullOrWhiteSpace(result.Value))
            {
                return null;
            }

            _memory = JsonSerializer.Deserialize<StoredAuthTokens>(result.Value);
            return _memory;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or JsonException)
        {
            _logger.LogDebug(ex, "Token store session read skipped.");
            return _memory;
        }
    }

    public async Task SetAsync(StoredAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _memory = tokens;
        _loaded = true;

        try
        {
            var json = JsonSerializer.Serialize(tokens);
            await _sessionStorage.SetAsync(StorageKey, json);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException)
        {
            _logger.LogDebug(ex, "Token store session write skipped; using circuit memory only.");
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _memory = null;
        _loaded = true;

        try
        {
            await _sessionStorage.DeleteAsync(StorageKey);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException)
        {
            _logger.LogDebug(ex, "Token store session clear skipped.");
        }
    }
}
