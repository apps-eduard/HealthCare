namespace HealthCare.Application.Identity;

/// <summary>
/// Revokes refresh tokens and updates the Identity security stamp so stale sessions lose access.
/// </summary>
public interface ISecuritySessionInvalidationService
{
    /// <returns>Number of refresh tokens revoked.</returns>
    Task<int> InvalidateUserSessionsAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken = default);
}
