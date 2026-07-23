namespace HealthCare.Contracts.Identity;

/// <summary>
/// Stable auth error codes returned via Problem Details extensions (never include tokens).
/// </summary>
public static class AuthErrorCodes
{
    public const string InvalidCredentials = "auth.invalid_credentials";
    public const string AccountDisabled = "auth.account_disabled";
    public const string AccountLocked = "auth.account_locked";
    public const string OrganizationInactive = "auth.organization_inactive";
    public const string ClinicInactive = "auth.clinic_inactive";
    public const string InvalidRefreshToken = "auth.invalid_refresh_token";
    public const string ExpiredRefreshToken = "auth.expired_refresh_token";
    public const string RevokedRefreshToken = "auth.revoked_refresh_token";
    public const string ReusedRefreshToken = "auth.reused_refresh_token";
    public const string EmailNotConfirmed = "auth.email_not_confirmed";
    public const string InvalidConfirmationToken = "auth.invalid_confirmation_token";
    public const string RegistrationFailed = "auth.registration_failed";
}
