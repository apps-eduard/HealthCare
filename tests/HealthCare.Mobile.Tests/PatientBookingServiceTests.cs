using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Booking;
using HealthCare.Mobile.Core.Discovery;
using HealthCare.Mobile.Core.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class PatientBookingServiceTests
{
    [Fact]
    public void IsSelectionReady_Requires_Clinic_Doctor_And_Slot()
    {
        var sut = CreateSut(new BookingFakeApi());
        var incomplete = new DiscoverySelection { ClinicCode = "c" };
        sut.IsSelectionReady(incomplete).Should().BeFalse();

        var ready = ReadySelection();
        sut.IsSelectionReady(ready).Should().BeTrue();
    }

    [Fact]
    public void ValidateReason_Enforces_Max_Length()
    {
        var sut = CreateSut(new BookingFakeApi());
        sut.ValidateReason(null).Should().BeNull();
        sut.ValidateReason(new string('x', PatientBookingLimits.MaxReasonLength + 1)).Should().NotBeNull();
    }

    [Fact]
    public void BuildRequest_Uses_Slot_StartUtc_And_Duration()
    {
        var sut = CreateSut(new BookingFakeApi());
        var selection = ReadySelection();
        var request = sut.BuildRequest(selection, "  checkup  ");

        request.ClinicCode.Should().Be("dev-clinic-a");
        request.DoctorStaffMemberId.Should().Be(selection.DoctorStaffMemberId!.Value);
        request.AppointmentDateUtc.Should().Be(selection.SelectedSlot!.StartUtc);
        request.DurationMinutes.Should().Be(30);
        request.Reason.Should().Be("checkup");
        typeof(CreatePatientAppointmentRequest).GetProperty("PatientId").Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_Calls_Api_Once_Without_App_Retry()
    {
        var api = new BookingFakeApi
        {
            CreateResult = ApiResult<AppointmentResponse>.Success(CreateResponse()),
        };
        var sut = CreateSut(api);
        var request = sut.BuildRequest(ReadySelection(), null);

        var first = await sut.SubmitAsync(request);
        first.IsSuccess.Should().BeTrue();
        api.CreateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task SubmitAsync_Surfaces_Timeout_Without_Retry()
    {
        var api = new BookingFakeApi
        {
            CreateResult = ApiResult<AppointmentResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Timeout,
                Title = "Timeout",
            }),
        };
        var sut = CreateSut(api);

        var result = await sut.SubmitAsync(sut.BuildRequest(ReadySelection(), null));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Kind.Should().Be(ApiErrorKind.Timeout);
        api.CreateCallCount.Should().Be(1);
    }

    [Fact]
    public void ToReceipt_Maps_Requested_Without_Exposing_Ids_In_Receipt_Model_Usage()
    {
        var sut = CreateSut(new BookingFakeApi());
        var selection = ReadySelection();
        var response = CreateResponse();
        var receipt = sut.ToReceipt(response, selection, selection.SelectedSlot);

        receipt.Status.Should().Be("Requested");
        receipt.Source.Should().Be("Patient");
        receipt.ClinicName.Should().Be("Dev Clinic A");
        receipt.DoctorDisplayName.Should().Be("Dr Test");
    }

    [Fact]
    public void MapConflictMessage_Uses_Safe_Slot_Conflict_Copy()
    {
        var sut = CreateSut(new BookingFakeApi());
        var message = sut.MapConflictMessage(new ApiProblem
        {
            Kind = ApiErrorKind.Conflict,
            ErrorCode = AppointmentErrorCodes.SlotConflict,
        });

        message.Should().Contain("no longer available");
        message.Should().NotContain(AppointmentErrorCodes.SlotConflict);
    }

    [Fact]
    public void Changing_Clinic_Clears_Doctor_And_Slot_For_Booking()
    {
        var state = new DiscoveryStateService();
        state.SelectClinic("a", "A");
        state.SelectDoctor(Guid.NewGuid(), "Dr", "Cardio");
        state.SelectSlot(DateOnly.FromDateTime(DateTime.Today), CreateSlot());

        state.SelectClinic("b", "B");

        state.Current.IsReadyForBooking.Should().BeFalse();
        state.Current.DoctorStaffMemberId.Should().BeNull();
        state.Current.SelectedSlot.Should().BeNull();
    }

    [Fact]
    public void Booking_Receipt_Clears_Independently()
    {
        var store = new BookingReceiptStore();
        store.Set(new BookingReceipt { Status = "Requested", AppointmentDateUtc = DateTimeOffset.UtcNow });
        store.LastSuccess.Should().NotBeNull();
        store.Clear();
        store.LastSuccess.Should().BeNull();
    }

    [Theory]
    [InlineData("/discovery/booking-review", true)]
    [InlineData("/discovery/booking-success", true)]
    [InlineData("/discovery/booking-next", true)]
    [InlineData("/sign-in", false)]
    public void Booking_Routes_Require_Authentication(string path, bool required)
    {
        PatientRoutes.RequiresAuthentication(path).Should().Be(required);
    }

    [Fact]
    public void SlotDisplay_And_Request_Keep_Utc_Authoritative()
    {
        var slot = new AvailableSlotResponse
        {
            StartUtc = DateTimeOffset.Parse("2026-07-26T07:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-07-26T07:30:00Z"),
            StartLocal = "10:00",
            EndLocal = "10:30",
            DurationMinutes = 30,
            TimeZoneId = "Asia/Riyadh",
        };
        SlotDisplay.FormatRange(slot).Should().Contain("10:00");
        SlotDisplay.FormatRange(slot).Should().Contain("Asia/Riyadh");

        var selection = ReadySelection() with { SelectedSlot = slot };
        var request = CreateSut(new BookingFakeApi()).BuildRequest(selection, null);
        request.AppointmentDateUtc.Should().Be(slot.StartUtc);
    }

    private static PatientBookingService CreateSut(BookingFakeApi api) =>
        new(api, NullLogger<PatientBookingService>.Instance);

    private static DiscoverySelection ReadySelection()
    {
        var doctorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new DiscoverySelection
        {
            ClinicCode = "dev-clinic-a",
            ClinicName = "Dev Clinic A",
            ClinicCity = "Riyadh",
            IsEnrolled = true,
            DoctorStaffMemberId = doctorId,
            DoctorDisplayName = "Dr Test",
            DoctorSpecialty = "General",
            SlotDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2)),
            SelectedSlot = CreateSlot(),
        };
    }

    private static AvailableSlotResponse CreateSlot() =>
        new()
        {
            StartUtc = DateTimeOffset.UtcNow.AddDays(2),
            EndUtc = DateTimeOffset.UtcNow.AddDays(2).AddMinutes(30),
            StartLocal = "09:00",
            EndLocal = "09:30",
            DurationMinutes = 30,
            TimeZoneId = "Asia/Riyadh",
        };

    private static AppointmentResponse CreateResponse() =>
        new()
        {
            Id = Guid.NewGuid(),
            Status = "Requested",
            Source = "Patient",
            AppointmentDateUtc = DateTimeOffset.UtcNow.AddDays(2),
            DurationMinutes = 30,
            ClinicName = "Dev Clinic A",
            ClinicSlug = "dev-clinic-a",
            DoctorDisplayName = "Dr Test",
            ClinicTimeZoneId = "Asia/Riyadh",
        };

    private sealed class BookingFakeApi : IHealthCareApiClient
    {
        public int CreateCallCount { get; private set; }

        public ApiResult<AppointmentResponse> CreateResult { get; set; } =
            ApiResult<AppointmentResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

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
            CancellationToken cancellationToken = default)
        {
            CreateCallCount++;
            return Task.FromResult(CreateResult);
        }
    }
}
