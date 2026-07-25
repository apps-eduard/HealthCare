using HealthCare.Mobile.Core.Storage;
using Microsoft.Extensions.Logging;

namespace HealthCare.Mobile.Storage;

public sealed class MauiSecureTokenStore : ISecureTokenStore
{
    private static readonly string[] SessionKeys =
    [
        SecureTokenKeys.AccessToken,
        SecureTokenKeys.RefreshToken,
        SecureTokenKeys.AccessTokenExpiresAtUtc,
        SecureTokenKeys.RefreshTokenExpiresAtUtc,
    ];

    private readonly ILogger<MauiSecureTokenStore> _logger;

    public MauiSecureTokenStore(ILogger<MauiSecureTokenStore> logger)
    {
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage get failed for key category {KeyCategory}.", Classify(key));
            return null;
        }
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SecureStorage set failed for key category {KeyCategory}.", Classify(key));
            throw;
        }
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage remove failed for key category {KeyCategory}.", Classify(key));
        }

        return Task.CompletedTask;
    }

    public async Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in SessionKeys)
        {
            await RemoveAsync(key, cancellationToken);
        }
    }

    private static string Classify(string key) =>
        key switch
        {
            SecureTokenKeys.AccessToken => "access_token",
            SecureTokenKeys.RefreshToken => "refresh_token",
            SecureTokenKeys.AccessTokenExpiresAtUtc => "access_expires",
            SecureTokenKeys.RefreshTokenExpiresAtUtc => "refresh_expires",
            _ => "other",
        };
}
