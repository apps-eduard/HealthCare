using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Core.Booking;
using HealthCare.Mobile.Core.Discovery;
using HealthCare.Mobile.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class PatientAuthenticationServiceTests
{
    [Fact]
    public async Task SignInAsync_Succeeds_When_Me_Confirms_Linked_Patient()
    {
        var session = CreateSession();
        var api = new FakeApiClient(session)
        {
            LoginResult = ApiResult<AuthTokenResponse>.Success(CreateTokens()),
            MeResult = ApiResult<CurrentUserResponse>.Success(CreatePatientUser()),
        };
        var sut = CreateSut(api, session);

        var result = await sut.SignInAsync(new LoginRequest { Email = "p@example.com", Password = "x" });

        result.Status.Should().Be(SignInStatus.Success);
        session.IsPatientReady.Should().BeTrue();
        session.Current.CurrentUser!.Email.Should().Be("p@example.com");
    }

    [Fact]
    public async Task SignInAsync_Maps_Invalid_Credentials()
    {
        var session = CreateSession();
        var api = new FakeApiClient(session)
        {
            LoginResult = ApiResult<AuthTokenResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Unauthorized,
                ErrorCode = AuthErrorCodes.InvalidCredentials,
                Title = "Invalid",
            }),
        };
        var sut = CreateSut(api, session);

        var result = await sut.SignInAsync(new LoginRequest { Email = "p@example.com", Password = "bad" });

        result.Status.Should().Be(SignInStatus.Failed);
        result.Problem!.ErrorCode.Should().Be(AuthErrorCodes.InvalidCredentials);
        result.Problem.UserMessage.Should().Contain("Invalid email or password");
    }

    [Fact]
    public async Task SignInAsync_Maps_Unconfirmed_Email()
    {
        var session = CreateSession();
        var api = new FakeApiClient(session)
        {
            LoginResult = ApiResult<AuthTokenResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Forbidden,
                ErrorCode = AuthErrorCodes.EmailNotConfirmed,
                Title = "Confirm",
            }),
        };
        var sut = CreateSut(api, session);

        var result = await sut.SignInAsync(new LoginRequest { Email = "p@example.com", Password = "x" });

        result.Status.Should().Be(SignInStatus.Failed);
        result.Problem!.UserMessage.Should().Contain("Confirm your email");
    }

    [Fact]
    public async Task SignInAsync_Clears_Session_When_Linkage_Missing()
    {
        var session = CreateSession();
        var api = new FakeApiClient(session)
        {
            LoginResult = ApiResult<AuthTokenResponse>.Success(CreateTokens()),
            MeResult = ApiResult<CurrentUserResponse>.Success(new CurrentUserResponse
            {
                UserId = Guid.NewGuid(),
                Email = "p@example.com",
                Roles = ["PATIENT"],
                HasLinkedPatient = false,
                PatientId = null,
                Permissions = [],
            }),
        };
        var sut = CreateSut(api, session);

        var result = await sut.SignInAsync(new LoginRequest { Email = "p@example.com", Password = "x" });

        result.Status.Should().Be(SignInStatus.LinkageRejected);
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task SignInAsync_Keeps_Tokens_When_Me_Is_Offline()
    {
        var session = CreateSession();
        var api = new FakeApiClient(session)
        {
            LoginResult = ApiResult<AuthTokenResponse>.Success(CreateTokens()),
            MeResult = ApiResult<CurrentUserResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Network,
                Title = "Network",
            }),
        };
        var sut = CreateSut(api, session);

        var result = await sut.SignInAsync(new LoginRequest { Email = "p@example.com", Password = "x" });

        result.Status.Should().Be(SignInStatus.OfflineAfterLogin);
        session.IsAuthenticated.Should().BeTrue();
        session.IsPatientReady.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreSessionAsync_Returns_Anonymous_Without_Tokens()
    {
        var session = CreateSession();
        var sut = CreateSut(new FakeApiClient(session), session);
        var result = await sut.RestoreSessionAsync();
        result.Status.Should().Be(SessionRestoreStatus.Anonymous);
    }

    [Fact]
    public async Task RestoreSessionAsync_Authenticates_Valid_Stored_Session()
    {
        var session = CreateSession();
        await session.SetSessionAsync(CreateTokens());
        var api = new FakeApiClient(session)
        {
            MeResult = ApiResult<CurrentUserResponse>.Success(CreatePatientUser()),
        };
        var sut = CreateSut(api, session);

        var result = await sut.RestoreSessionAsync();

        result.Status.Should().Be(SessionRestoreStatus.AuthenticatedPatient);
        session.IsPatientReady.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreSessionAsync_Offline_Does_Not_Clear_Tokens()
    {
        var session = CreateSession();
        await session.SetSessionAsync(CreateTokens());
        var api = new FakeApiClient(session)
        {
            MeResult = ApiResult<CurrentUserResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Network,
                Title = "down",
            }),
        };
        var sut = CreateSut(api, session);

        var result = await sut.RestoreSessionAsync();

        result.Status.Should().Be(SessionRestoreStatus.OfflineWithTokens);
        session.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreSessionAsync_Clears_Invalid_Session()
    {
        var session = CreateSession();
        await session.SetSessionAsync(CreateTokens());
        var api = new FakeApiClient(session)
        {
            MeResult = ApiResult<CurrentUserResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Unauthorized,
                Title = "expired",
            }),
        };
        var sut = CreateSut(api, session);

        var result = await sut.RestoreSessionAsync();

        result.Status.Should().Be(SessionRestoreStatus.InvalidSessionCleared);
        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task SignOutAsync_Clears_Local_Session()
    {
        var session = CreateSession();
        await session.SetSessionAsync(CreateTokens(), CreatePatientUser());
        var api = new FakeApiClient(session) { LogoutShouldClear = true };
        var sut = CreateSut(api, session);

        await sut.SignOutAsync();

        session.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task RegisterAsync_Returns_Api_Result()
    {
        var session = CreateSession();
        var api = new FakeApiClient(session)
        {
            RegisterResult = ApiResult<PatientRegisterResponse>.Success(new PatientRegisterResponse
            {
                Message = "ok",
                RequiresEmailConfirmation = true,
            }),
        };
        var sut = CreateSut(api, session);

        var result = await sut.RegisterAsync(new PatientRegisterRequest
        {
            Email = "a@b.c",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
            FirstName = "A",
            LastName = "B",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.RequiresEmailConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task SignOutAsync_Clears_Discovery_Selection()
    {
        var session = CreateSession();
        var discovery = new DiscoveryStateService();
        discovery.SelectClinic("clinic-a", "Clinic A");
        var api = new FakeApiClient(session) { LogoutShouldClear = true };
        var sut = new PatientAuthenticationService(
            api,
            session,
            new FakeRefresher(_ => Task.FromResult(true)),
            discovery,
            new BookingReceiptStore(),
            NullLogger<PatientAuthenticationService>.Instance);

        await sut.SignOutAsync();

        discovery.Current.ClinicCode.Should().BeNull();
    }

    private static PatientAuthenticationService CreateSut(FakeApiClient api, IAuthSessionService session) =>
        new(
            api,
            session,
            new FakeRefresher(_ => Task.FromResult(true)),
            new DiscoveryStateService(),
            new BookingReceiptStore(),
            NullLogger<PatientAuthenticationService>.Instance);

    private static AuthSessionService CreateSession() =>
        new(new InMemorySecureTokenStore(), NullLogger<AuthSessionService>.Instance);

    private static AuthTokenResponse CreateTokens() =>
        new()
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        };

    private static CurrentUserResponse CreatePatientUser() =>
        new()
        {
            UserId = Guid.NewGuid(),
            Email = "p@example.com",
            Roles = ["PATIENT"],
            PatientId = Guid.NewGuid(),
            HasLinkedPatient = true,
            Permissions = [],
        };

    private sealed class FakeRefresher : ITokenRefresher
    {
        private readonly Func<string, Task<bool>> _impl;

        public FakeRefresher(Func<string, Task<bool>> impl) => _impl = impl;

        public Task<bool> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
            _impl(refreshToken);
    }

    private sealed class FakeApiClient : IHealthCareApiClient
    {
        private readonly IAuthSessionService _session;

        public FakeApiClient(IAuthSessionService session) => _session = session;

        public ApiResult<AuthTokenResponse> LoginResult { get; set; } =
            ApiResult<AuthTokenResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

        public ApiResult<CurrentUserResponse> MeResult { get; set; } =
            ApiResult<CurrentUserResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

        public ApiResult<PatientRegisterResponse> RegisterResult { get; set; } =
            ApiResult<PatientRegisterResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

        public bool LogoutShouldClear { get; set; } = true;

        public Task<ApiResult<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PatientRegisterResponse>> RegisterPatientAsync(
            PatientRegisterRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RegisterResult);

        public Task<ApiResult<ConfirmEmailResponse>> ConfirmEmailAsync(
            ConfirmEmailRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<ResendConfirmationResponse>> ResendConfirmationAsync(
            ResendConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public async Task<ApiResult<AuthTokenResponse>> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            if (LoginResult.IsSuccess && LoginResult.Value is not null)
            {
                await _session.SetSessionAsync(LoginResult.Value, cancellationToken: cancellationToken);
            }

            return LoginResult;
        }

        public Task<ApiResult<CurrentUserResponse>> GetMeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(MeResult);

        public async Task<ApiResult<bool>> LogoutAsync(CancellationToken cancellationToken = default)
        {
            if (LogoutShouldClear)
            {
                await _session.ClearSessionAsync(cancellationToken);
            }

            return ApiResult<bool>.Success(true);
        }

        public Task<ApiResult<PatientProfileResponse>> GetPatientProfileAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PatientProfileResponse>> UpdatePatientProfileAsync(
            UpdatePatientProfileRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PagedResponse<PatientClinicListItemResponse>>> SearchClinicsAsync(
            PatientClinicSearchRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PatientClinicDetailResponse>> GetClinicAsync(
            string clinicCode,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<ClinicPatientEnrollmentResponse>> RegisterWithClinicAsync(
            RegisterPatientWithClinicRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<IReadOnlyList<ClinicDoctorResponse>>> ListDoctorsAsync(
            string clinicCode,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<IReadOnlyList<AvailableSlotResponse>>> GetAvailableSlotsAsync(
            string clinicCode,
            Guid staffMemberId,
            DateOnly date,
            int? durationMinutes = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<AppointmentResponse>> CreatePatientAppointmentAsync(
            CreatePatientAppointmentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PagedResponse<AppointmentResponse>>> ListPatientAppointmentsAsync(
            AppointmentListQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<AppointmentResponse>> GetAppointmentAsync(
            Guid appointmentId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<AppointmentResponse>> CancelAppointmentAsync(
            Guid appointmentId,
            AppointmentActionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<AppointmentResponse>> RescheduleAppointmentAsync(
            Guid appointmentId,
            RescheduleAppointmentRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
