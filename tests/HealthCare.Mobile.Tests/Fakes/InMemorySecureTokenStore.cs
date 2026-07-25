using HealthCare.Mobile.Core.Storage;

namespace HealthCare.Mobile.Tests.Fakes;

internal sealed class InMemorySecureTokenStore : ISecureTokenStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Snapshot => _values;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _values.Remove(key);
        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var key in new[]
                 {
                     SecureTokenKeys.AccessToken,
                     SecureTokenKeys.RefreshToken,
                     SecureTokenKeys.AccessTokenExpiresAtUtc,
                     SecureTokenKeys.RefreshTokenExpiresAtUtc,
                 })
        {
            _values.Remove(key);
        }

        return Task.CompletedTask;
    }
}
