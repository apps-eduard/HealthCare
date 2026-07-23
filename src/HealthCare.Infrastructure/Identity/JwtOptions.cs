namespace HealthCare.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; set; }

    public required string Audience { get; set; }

    public required string SigningKey { get; set; }

    /// <summary>
    /// Access token lifetime in minutes. Default: 15.
    /// </summary>
    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token lifetime in days. Default: 7.
    /// </summary>
    public int RefreshTokenLifetimeDays { get; set; } = 7;
}
