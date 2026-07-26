using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Core.Booking;
using HealthCare.Mobile.Core.Discovery;
using HealthCare.Mobile.Core.Navigation;
using HealthCare.Mobile.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

/// <summary>PM-7: mobile route guards and logout state isolation.</summary>
public sealed class PatientMobileSecurityTests
{
    [Theory]
    [InlineData("/home")]
    [InlineData("/profile")]
    [InlineData("/profile/edit")]
    [InlineData("/clinics")]
    [InlineData("/clinics/dev-clinic-a")]
    [InlineData("/clinics/dev-clinic-a/doctors")]
    [InlineData("/appointments")]
    [InlineData("/appointments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    [InlineData("/appointments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/reschedule")]
    [InlineData("/discovery/booking-review")]
    [InlineData("/discovery/booking-success")]
    public void Protected_Patient_Routes_Require_Authentication(string path)
    {
        PatientRoutes.RequiresAuthentication(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("/sign-in")]
    [InlineData("/register")]
    [InlineData("/confirm-email")]
    [InlineData("/connectivity")]
    [InlineData("/")]
    public void Guest_And_Public_Routes_Do_Not_Require_Authentication(string path)
    {
        PatientRoutes.RequiresAuthentication(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("/sign-in", true)]
    [InlineData("/register", true)]
    [InlineData("/home", false)]
    [InlineData("/appointments", false)]
    public void Guest_Only_Routes_Are_Isolated_From_Authenticated_Shell(string path, bool guestOnly)
    {
        PatientRoutes.IsGuestOnly(path).Should().Be(guestOnly);
    }

    [Fact]
    public void Mobile_Does_Not_Expose_Staff_Or_Admin_Routes()
    {
        PatientRoutes.Normalize("/staff").Should().Be("/staff");
        PatientRoutes.RequiresAuthentication("/staff").Should().BeFalse(
            because: "staff routes are not part of the Patient app; absence of auth gate is not an access grant");
        PatientRoutes.RequiresAuthentication("/organization/dashboard").Should().BeFalse();
        PatientRoutes.RequiresAuthentication("/clinic/audit-logs").Should().BeFalse();
        PatientRoutes.RequiresAuthentication("/medical-notes").Should().BeFalse();
    }

    [Fact]
    public async Task SignOut_Clears_Tokens_User_Discovery_And_Booking_Receipt()
    {
        var session = new AuthSessionService(
            new InMemorySecureTokenStore(),
            NullLogger<AuthSessionService>.Instance);
        var discovery = new DiscoveryStateService();
        var receipts = new BookingReceiptStore();

        await session.SetSessionAsync(
            new AuthTokenResponse
            {
                AccessToken = "access",
                RefreshToken = "refresh",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            },
            new CurrentUserResponse
            {
                UserId = Guid.NewGuid(),
                Email = "p@example.com",
                Roles = ["PATIENT"],
                PatientId = Guid.NewGuid(),
                HasLinkedPatient = true,
                Permissions = [],
            });

        discovery.SelectClinic("dev-clinic-a", "Clinic");
        discovery.SelectDoctor(Guid.NewGuid(), "Dr");
        discovery.SelectSlot(
            DateOnly.FromDateTime(DateTime.Today),
            new AvailableSlotResponse
            {
                StartUtc = DateTimeOffset.UtcNow.AddDays(2),
                EndUtc = DateTimeOffset.UtcNow.AddDays(2).AddMinutes(30),
                DurationMinutes = 30,
            });
        receipts.Set(new BookingReceipt
        {
            Status = "Requested",
            ClinicCode = "dev-clinic-a",
            AppointmentDateUtc = DateTimeOffset.UtcNow.AddDays(2),
            DurationMinutes = 30,
        });

        var api = new LogoutClearingApi(session);
        var sut = new PatientAuthenticationService(
            api,
            session,
            new AlwaysRefresh(),
            discovery,
            receipts,
            NullLogger<PatientAuthenticationService>.Instance);

        await sut.SignOutAsync();

        session.IsAuthenticated.Should().BeFalse();
        session.IsPatientReady.Should().BeFalse();
        session.Current.AccessToken.Should().BeNull();
        session.Current.CurrentUser.Should().BeNull();
        discovery.Current.ClinicCode.Should().BeNull();
        discovery.Current.HasSlot.Should().BeFalse();
        receipts.LastSuccess.Should().BeNull();
    }

    [Fact]
    public async Task Unlinked_SignIn_Does_Not_Enter_Patient_Ready_State()
    {
        var session = new AuthSessionService(
            new InMemorySecureTokenStore(),
            NullLogger<AuthSessionService>.Instance);
        var api = new UnlinkedLoginApi(session);
        var sut = new PatientAuthenticationService(
            api,
            session,
            new AlwaysRefresh(),
            new DiscoveryStateService(),
            new BookingReceiptStore(),
            NullLogger<PatientAuthenticationService>.Instance);

        var result = await sut.SignInAsync(new LoginRequest { Email = "u@example.com", Password = "x" });

        result.Status.Should().Be(SignInStatus.LinkageRejected);
        session.IsPatientReady.Should().BeFalse();
        session.IsAuthenticated.Should().BeFalse();
    }

    private sealed class AlwaysRefresh : ITokenRefresher
    {
        public Task<bool> TryRefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class LogoutClearingApi : IHealthCareApiClient
    {
        private readonly IAuthSessionService _session;

        public LogoutClearingApi(IAuthSessionService session) => _session = session;

        public Task<ApiResult<bool>> LogoutAsync(CancellationToken cancellationToken = default)
        {
            return Clear();
            async Task<ApiResult<bool>> Clear()
            {
                await _session.ClearSessionAsync(cancellationToken);
                return ApiResult<bool>.Success(true);
            }
        }

        public Task<ApiResult<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PatientRegisterResponse>> RegisterPatientAsync(
            PatientRegisterRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<ConfirmEmailResponse>> ConfirmEmailAsync(
            ConfirmEmailRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<ResendConfirmationResponse>> ResendConfirmationAsync(
            ResendConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<AuthTokenResponse>> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<CurrentUserResponse>> GetMeAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

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

    private sealed class UnlinkedLoginApi : IHealthCareApiClient
    {
        private readonly IAuthSessionService _session;

        public UnlinkedLoginApi(IAuthSessionService session) => _session = session;

        public async Task<ApiResult<AuthTokenResponse>> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            var tokens = new AuthTokenResponse
            {
                AccessToken = "a",
                RefreshToken = "r",
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                RefreshTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
            };
            await _session.SetSessionAsync(tokens, cancellationToken: cancellationToken);
            return ApiResult<AuthTokenResponse>.Success(tokens);
        }

        public Task<ApiResult<CurrentUserResponse>> GetMeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ApiResult<CurrentUserResponse>.Success(new CurrentUserResponse
            {
                UserId = Guid.NewGuid(),
                Email = "u@example.com",
                Roles = ["PATIENT"],
                PatientId = null,
                HasLinkedPatient = false,
                Permissions = [],
            }));

        public Task<ApiResult<HealthStatusDto>> GetHealthAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PatientRegisterResponse>> RegisterPatientAsync(
            PatientRegisterRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<ConfirmEmailResponse>> ConfirmEmailAsync(
            ConfirmEmailRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<ResendConfirmationResponse>> ResendConfirmationAsync(
            ResendConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<bool>> LogoutAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

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
