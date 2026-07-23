namespace HealthCare.Domain.Identity;

/// <summary>
/// Persisted refresh token. Only the hash is stored — never the raw token value.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw refresh token (hex or Base64).
    /// </summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Shared identifier for a refresh-token rotation family.
    /// </summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? CreatedByIp { get; set; }

    public string? CreatedByUserAgent { get; set; }

    public string? RevokedReason { get; set; }

    public ApplicationUser? User { get; set; }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired(DateTimeOffset utcNow) => utcNow >= ExpiresAtUtc;
}
