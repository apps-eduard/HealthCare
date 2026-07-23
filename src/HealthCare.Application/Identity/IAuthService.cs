using HealthCare.Contracts.Identity;

namespace HealthCare.Application.Identity;

public sealed record AuthClientContext(string? IpAddress, string? UserAgent);

public interface IAuthService
{
    Task<AuthTokenResponse> LoginAsync(
        LoginRequest request,
        AuthClientContext client,
        CancellationToken cancellationToken = default);

    Task<AuthTokenResponse> RefreshAsync(
        RefreshTokenRequest request,
        AuthClientContext client,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        LogoutRequest request,
        CancellationToken cancellationToken = default);
}
