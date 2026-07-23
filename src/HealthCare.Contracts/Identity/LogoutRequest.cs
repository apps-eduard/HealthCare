namespace HealthCare.Contracts.Identity;

public sealed class LogoutRequest
{
    public required string RefreshToken { get; init; }
}
