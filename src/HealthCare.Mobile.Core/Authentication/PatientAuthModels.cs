using HealthCare.Contracts.Identity;
using HealthCare.Mobile.Core.Api;

namespace HealthCare.Mobile.Core.Authentication;

public enum SessionRestoreStatus
{
    Anonymous,
    AuthenticatedPatient,
    OfflineWithTokens,
    InvalidSessionCleared,
}

public sealed class SessionRestoreResult
{
    public required SessionRestoreStatus Status { get; init; }

    public ApiProblem? Problem { get; init; }

    public CurrentUserResponse? User { get; init; }

    public static SessionRestoreResult Anonymous() =>
        new() { Status = SessionRestoreStatus.Anonymous };

    public static SessionRestoreResult Authenticated(CurrentUserResponse user) =>
        new() { Status = SessionRestoreStatus.AuthenticatedPatient, User = user };

    public static SessionRestoreResult Offline(ApiProblem? problem = null) =>
        new()
        {
            Status = SessionRestoreStatus.OfflineWithTokens,
            Problem = problem ?? new ApiProblem
            {
                Kind = ApiErrorKind.Network,
                Title = "Offline",
            },
        };

    public static SessionRestoreResult Cleared(ApiProblem? problem = null) =>
        new() { Status = SessionRestoreStatus.InvalidSessionCleared, Problem = problem };
}

public enum SignInStatus
{
    Success,
    Failed,
    OfflineAfterLogin,
    LinkageRejected,
}

public sealed class SignInResult
{
    public required SignInStatus Status { get; init; }

    public CurrentUserResponse? User { get; init; }

    public ApiProblem? Problem { get; init; }

    public static SignInResult Success(CurrentUserResponse user) =>
        new() { Status = SignInStatus.Success, User = user };

    public static SignInResult Failed(ApiProblem problem) =>
        new() { Status = SignInStatus.Failed, Problem = problem };

    public static SignInResult Offline(ApiProblem problem) =>
        new() { Status = SignInStatus.OfflineAfterLogin, Problem = problem };

    public static SignInResult LinkageRejected(ApiProblem problem) =>
        new() { Status = SignInStatus.LinkageRejected, Problem = problem };
}

public interface IPatientAuthenticationService
{
    Task<ApiResult<PatientRegisterResponse>> RegisterAsync(
        PatientRegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ConfirmEmailResponse>> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResult<ResendConfirmationResponse>> ResendConfirmationAsync(
        ResendConfirmationRequest request,
        CancellationToken cancellationToken = default);

    Task<SignInResult> SignInAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task<SessionRestoreResult> RestoreSessionAsync(CancellationToken cancellationToken = default);
}
