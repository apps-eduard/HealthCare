namespace HealthCare.Application.Identity;

/// <summary>
/// Sends account emails. Implementations must never log tokens or full confirmation/reset links.
/// </summary>
public interface IAccountEmailSender
{
    Task SendEmailConfirmationAsync(
        string email,
        string confirmationToken,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(
        string email,
        string resetToken,
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

/// <summary>
/// Development/test-only capture of password-reset tokens for manual verification.
/// </summary>
public interface IDevelopmentPasswordResetTokenStore
{
    void Store(string email, string token);

    bool TryGet(string email, out string? token);

    void Clear(string email);
}
