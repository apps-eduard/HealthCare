namespace HealthCare.Web.Auth;

public sealed class StoredAuthTokens
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public required DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
}

public interface IApiTokenStore
{
    Task<StoredAuthTokens?> GetAsync(CancellationToken cancellationToken = default);

    Task SetAsync(StoredAuthTokens tokens, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
