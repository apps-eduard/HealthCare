namespace HealthCare.Application.Identity;

/// <summary>
/// Sends account emails. Implementations must never log tokens or full confirmation links.
/// </summary>
public interface IAccountEmailSender
{
    Task SendEmailConfirmationAsync(
        string email,
        string confirmationToken,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Development/test-only capture of confirmation tokens for manual verification.
/// </summary>
public interface IDevelopmentConfirmationTokenStore
{
    void Store(string email, string token);

    bool TryGet(string email, out string? token);

    void Clear(string email);
}
