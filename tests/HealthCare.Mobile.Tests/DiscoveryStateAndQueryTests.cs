using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Discovery;
using HealthCare.Mobile.Core.Navigation;

namespace HealthCare.Mobile.Tests;

public sealed class DiscoveryStateServiceTests
{
    [Fact]
    public void SelectClinic_Clears_Doctor_And_Slot()
    {
        var sut = new DiscoveryStateService();
        sut.SelectClinic("clinic-a", "Clinic A");
        sut.SelectDoctor(Guid.NewGuid(), "Dr X");
        sut.SelectSlot(DateOnly.FromDateTime(DateTime.Today), CreateSlot());

        sut.SelectClinic("clinic-b", "Clinic B");

        sut.Current.ClinicCode.Should().Be("clinic-b");
        sut.Current.DoctorStaffMemberId.Should().BeNull();
        sut.Current.SelectedSlot.Should().BeNull();
        sut.Current.HasSlot.Should().BeFalse();
    }

    [Fact]
    public void SelectDoctor_Clears_Slot_When_Doctor_Changes()
    {
        var sut = new DiscoveryStateService();
        sut.SelectClinic("clinic-a", "Clinic A");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        sut.SelectDoctor(first, "Dr One");
        sut.SelectSlot(DateOnly.FromDateTime(DateTime.Today), CreateSlot());

        sut.SelectDoctor(second, "Dr Two");

        sut.Current.DoctorStaffMemberId.Should().Be(second);
        sut.Current.SelectedSlot.Should().BeNull();
    }

    [Fact]
    public void Clear_Resets_All_Selection()
    {
        var sut = new DiscoveryStateService();
        sut.SelectClinic("clinic-a", "Clinic A");
        sut.SelectDoctor(Guid.NewGuid(), "Dr X");
        sut.SelectSlot(DateOnly.FromDateTime(DateTime.Today), CreateSlot());

        sut.Clear();

        sut.Current.ClinicCode.Should().BeNull();
        sut.Current.HasSlot.Should().BeFalse();
    }

    [Fact]
    public void SlotDisplay_Prefers_Clinic_Local_Strings()
    {
        var text = SlotDisplay.FormatRange(new AvailableSlotResponse
        {
            StartUtc = DateTimeOffset.Parse("2026-07-25T07:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-07-25T07:30:00Z"),
            StartLocal = "10:00",
            EndLocal = "10:30",
            TimeZoneId = "Asia/Riyadh",
            DurationMinutes = 30,
        });

        text.Should().Contain("10:00");
        text.Should().Contain("10:30");
        text.Should().Contain("Asia/Riyadh");
    }

    [Theory]
    [InlineData("/clinics", true)]
    [InlineData("/clinics/dev-clinic-a", true)]
    [InlineData("/clinics/enroll", true)]
    [InlineData("/clinics/dev-clinic-a/doctors", true)]
    [InlineData("/discovery/booking-next", true)]
    [InlineData("/connectivity", false)]
    public void Discovery_Routes_Require_Authentication(string path, bool required)
    {
        PatientRoutes.RequiresAuthentication(path).Should().Be(required);
    }

    [Fact]
    public void BuildClinicSearchPath_Encodes_Search_And_Specialty()
    {
        var path = HealthCareApiClient.BuildClinicSearchPath(new PatientClinicSearchRequest
        {
            Search = " City A ",
            Specialty = "Cardio",
            Page = 2,
            PageSize = 10,
        });

        path.Should().StartWith("api/v1/patients/me/clinics?");
        path.Should().Contain("page=2");
        path.Should().Contain("pageSize=10");
        path.Should().Contain("search=City%20A");
        path.Should().Contain("specialty=Cardio");
    }

    [Fact]
    public async Task DiscoveryService_Normalizes_Enrollment_Code()
    {
        string? seen = null;
        var api = new CapturingApi();
        api.OnRegister = request =>
        {
            seen = request.ClinicCode;
            return ApiResult<ClinicPatientEnrollmentResponse>.Success(new ClinicPatientEnrollmentResponse
            {
                ClinicId = Guid.NewGuid(),
                PatientId = Guid.NewGuid(),
                LocalPatientNumber = "P-1",
                Status = "Active",
                AlreadyEnrolled = false,
            });
        };

        var sut = new PatientDiscoveryService(api);
        var result = await sut.EnrollAsync("  DEV-CLINIC-A ");

        result.IsSuccess.Should().BeTrue();
        seen.Should().Be("dev-clinic-a");
    }

    private static AvailableSlotResponse CreateSlot() =>
        new()
        {
            StartUtc = DateTimeOffset.UtcNow,
            EndUtc = DateTimeOffset.UtcNow.AddMinutes(30),
            StartLocal = "09:00",
            EndLocal = "09:30",
            DurationMinutes = 30,
            TimeZoneId = "UTC",
        };

    private sealed class CapturingApi : IHealthCareApiClient
    {
        public Func<RegisterPatientWithClinicRequest, ApiResult<ClinicPatientEnrollmentResponse>>? OnRegister { get; set; }

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
            Task.FromResult(OnRegister!(request));

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
    }
}
