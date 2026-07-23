namespace HealthCare.Contracts.Identity;

public sealed class RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}
