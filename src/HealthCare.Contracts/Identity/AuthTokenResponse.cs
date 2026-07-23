namespace HealthCare.Contracts.Identity;

public sealed class AuthTokenResponse
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTimeOffset AccessTokenExpiresAtUtc { get; init; }

    public required DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }

    public string TokenType { get; init; } = "Bearer";
}
