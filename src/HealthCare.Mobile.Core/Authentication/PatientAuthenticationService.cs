using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Discovery;
using Microsoft.Extensions.Logging;

namespace HealthCare.Mobile.Core.Authentication;

public sealed class PatientAuthenticationService : IPatientAuthenticationService
{
    private readonly IHealthCareApiClient _api;
    private readonly IAuthSessionService _session;
    private readonly ITokenRefresher _refresher;
    private readonly IDiscoveryStateService _discovery;
    private readonly ILogger<PatientAuthenticationService> _logger;

    public PatientAuthenticationService(
        IHealthCareApiClient api,
        IAuthSessionService session,
        ITokenRefresher refresher,
        IDiscoveryStateService discovery,
        ILogger<PatientAuthenticationService> logger)
    {
        _api = api;
        _session = session;
        _refresher = refresher;
        _discovery = discovery;
        _logger = logger;
    }

    public Task<ApiResult<PatientRegisterResponse>> RegisterAsync(
        PatientRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Patient registration submitted.");
        return _api.RegisterPatientAsync(request, cancellationToken);
    }

    public Task<ApiResult<ConfirmEmailResponse>> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Email confirmation attempted.");
        return _api.ConfirmEmailAsync(request, cancellationToken);
    }

    public Task<ApiResult<ResendConfirmationResponse>> ResendConfirmationAsync(
        ResendConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resend confirmation requested.");
        return _api.ResendConfirmationAsync(request, cancellationToken);
    }

    public async Task<SignInResult> SignInAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Patient sign-in attempted.");

        var login = await _api.LoginAsync(request, cancellationToken);
        if (!login.IsSuccess || login.Value is null)
        {
            return SignInResult.Failed(login.Error ?? new ApiProblem
            {
                Kind = ApiErrorKind.Unauthorized,
                Title = "Sign-in failed",
                ErrorCode = AuthErrorCodes.InvalidCredentials,
            });
        }

        var me = await _api.GetMeAsync(cancellationToken);
        if (!me.IsSuccess || me.Value is null)
        {
            if (me.Error?.Kind is ApiErrorKind.Network or ApiErrorKind.Timeout)
            {
                _logger.LogInformation("Sign-in succeeded but current-user resolution is offline.");
                return SignInResult.Offline(me.Error);
            }

            await _session.ClearSessionAsync(cancellationToken);
            return SignInResult.Failed(me.Error ?? new ApiProblem
            {
                Kind = ApiErrorKind.Unauthorized,
                Title = "Unable to resolve account",
            });
        }

        if (!PatientIdentityRules.IsEligiblePatientAccount(me.Value))
        {
            _logger.LogInformation("Sign-in rejected: Patient linkage or role validation failed.");
            await _session.ClearSessionAsync(cancellationToken);
            return SignInResult.LinkageRejected(new ApiProblem
            {
                Kind = ApiErrorKind.Forbidden,
                Title = "Account not ready",
                Detail = PatientIdentityRules.LinkageFailureMessage,
                ErrorCode = "patient.linkage_required",
            });
        }

        await _session.UpdateCurrentUserAsync(me.Value, cancellationToken);
        return SignInResult.Success(me.Value);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Patient sign-out requested.");
        await _api.LogoutAsync(cancellationToken);
        _discovery.Clear();
    }

    public async Task<SessionRestoreResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        await _session.InitializeAsync(cancellationToken);

        if (!_session.IsAuthenticated)
        {
            return SessionRestoreResult.Anonymous();
        }

        if (_session.Current.IsAccessTokenExpiredOrExpiring)
        {
            var refreshToken = _session.Current.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                await _session.ClearSessionAsync(cancellationToken);
                return SessionRestoreResult.Cleared(new ApiProblem
                {
                    Kind = ApiErrorKind.Unauthorized,
                    Title = "Session expired",
                });
            }

            try
            {
                var refreshed = await _refresher.TryRefreshAsync(refreshToken, cancellationToken);
                if (!refreshed)
                {
                    // Distinguishes auth rejection (cleared by refresher path below via session)
                    // from network: TokenRefresher returns false for both. Probe with GetMe if tokens remain.
                    if (!_session.IsAuthenticated)
                    {
                        return SessionRestoreResult.Cleared(new ApiProblem
                        {
                            Kind = ApiErrorKind.Unauthorized,
                            Title = "Session expired",
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogInformation(ex, "Session restore offline during refresh.");
                return SessionRestoreResult.Offline();
            }
        }

        var me = await _api.GetMeAsync(cancellationToken);
        if (!me.IsSuccess || me.Value is null)
        {
            if (me.Error?.Kind is ApiErrorKind.Network or ApiErrorKind.Timeout)
            {
                _logger.LogInformation("Session restore offline during /auth/me.");
                return SessionRestoreResult.Offline(me.Error);
            }

            await _session.ClearSessionAsync(cancellationToken);
            return SessionRestoreResult.Cleared(me.Error);
        }

        if (!PatientIdentityRules.IsEligiblePatientAccount(me.Value))
        {
            await _session.ClearSessionAsync(cancellationToken);
            return SessionRestoreResult.Cleared(new ApiProblem
            {
                Kind = ApiErrorKind.Forbidden,
                Title = "Account not ready",
                Detail = PatientIdentityRules.LinkageFailureMessage,
            });
        }

        await _session.UpdateCurrentUserAsync(me.Value, cancellationToken);
        return SessionRestoreResult.Authenticated(me.Value);
    }
}
