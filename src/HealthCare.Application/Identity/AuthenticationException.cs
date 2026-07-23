using HealthCare.Contracts.Identity;

namespace HealthCare.Application.Identity;

public sealed class AuthenticationException : Exception
{
    public AuthenticationException(string errorCode, string title, int statusCode = 401)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static AuthenticationException InvalidCredentials() =>
        new(AuthErrorCodes.InvalidCredentials, "Invalid email or password.");

    public static AuthenticationException AccountDisabled() =>
        new(AuthErrorCodes.AccountDisabled, "This account is disabled.", StatusCodes.Status403Forbidden);

    public static AuthenticationException AccountLocked() =>
        new(AuthErrorCodes.AccountLocked, "This account is temporarily locked.", StatusCodes.Status403Forbidden);

    public static AuthenticationException OrganizationInactive() =>
        new(AuthErrorCodes.OrganizationInactive, "The organization is inactive.", StatusCodes.Status403Forbidden);

    public static AuthenticationException ClinicInactive() =>
        new(AuthErrorCodes.ClinicInactive, "The clinic is inactive.", StatusCodes.Status403Forbidden);

    public static AuthenticationException InvalidRefreshToken() =>
        new(AuthErrorCodes.InvalidRefreshToken, "Invalid refresh token.");

    public static AuthenticationException ExpiredRefreshToken() =>
        new(AuthErrorCodes.ExpiredRefreshToken, "The refresh token has expired.");

    public static AuthenticationException RevokedRefreshToken() =>
        new(AuthErrorCodes.RevokedRefreshToken, "The refresh token has been revoked.");

    public static AuthenticationException ReusedRefreshToken() =>
        new(AuthErrorCodes.ReusedRefreshToken, "Refresh token reuse was detected. The token family has been revoked.");

    public static AuthenticationException EmailNotConfirmed() =>
        new(AuthErrorCodes.EmailNotConfirmed, "Email confirmation is required before sign-in.", StatusCodes.Status403Forbidden);

    public static AuthenticationException InvalidConfirmationToken() =>
        new(AuthErrorCodes.InvalidConfirmationToken, "The email confirmation token is invalid or has expired.");

    public static AuthenticationException RegistrationFailed() =>
        new(AuthErrorCodes.RegistrationFailed, "Registration could not be completed.", StatusCodes.Status400BadRequest);
}

// Avoid depending on ASP.NET abstractions in Application for StatusCodes constants.
file static class StatusCodes
{
    public const int Status403Forbidden = 403;
    public const int Status400BadRequest = 400;
}
