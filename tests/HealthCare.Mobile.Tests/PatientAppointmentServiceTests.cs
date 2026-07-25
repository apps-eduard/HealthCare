using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Appointments;
using HealthCare.Mobile.Core.Discovery;
using HealthCare.Mobile.Core.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class PatientAppointmentServiceTests
{
    [Fact]
    public void Upcoming_And_Previous_Use_Status_And_End_Time()
    {
        var now = DateTimeOffset.Parse("2026-07-25T12:00:00Z");
        var upcoming = Sample(now.AddHours(5), PatientAppointmentStatuses.Requested);
        var pastActive = Sample(now.AddHours(-3), PatientAppointmentStatuses.Confirmed, durationMinutes: 30);
        var cancelled = Sample(now.AddDays(2), PatientAppointmentStatuses.CancelledByPatient);
        var completed = Sample(now.AddDays(-1), PatientAppointmentStatuses.Completed);

        PatientAppointmentStatuses.IsUpcoming(upcoming, now).Should().BeTrue();
        PatientAppointmentStatuses.IsPrevious(upcoming, now).Should().BeFalse();
        PatientAppointmentStatuses.IsPrevious(pastActive, now).Should().BeTrue();
        PatientAppointmentStatuses.IsPrevious(cancelled, now).Should().BeTrue();
        PatientAppointmentStatuses.IsPrevious(completed, now).Should().BeTrue();
    }

    [Theory]
    [InlineData(PatientAppointmentStatuses.Requested, true)]
    [InlineData(PatientAppointmentStatuses.Confirmed, true)]
    [InlineData(PatientAppointmentStatuses.CheckedIn, false)]
    [InlineData(PatientAppointmentStatuses.InProgress, false)]
    [InlineData(PatientAppointmentStatuses.Completed, false)]
    [InlineData(PatientAppointmentStatuses.CancelledByPatient, false)]
    [InlineData(PatientAppointmentStatuses.CancelledByClinic, false)]
    [InlineData(PatientAppointmentStatuses.NoShow, false)]
    public void Action_Visibility_Follows_Status(string status, bool allowed)
    {
        var now = DateTimeOffset.Parse("2026-07-25T12:00:00Z");
        var appointment = Sample(now.AddHours(5), status);
        PatientAppointmentStatuses.CanCancel(appointment, now).Should().Be(allowed);
        PatientAppointmentStatuses.CanReschedule(appointment, now).Should().Be(allowed);
    }

    [Fact]
    public void Exact_Two_Hour_Cutoff_Allows_Mutation_Less_Than_Denies()
    {
        var now = DateTimeOffset.Parse("2026-07-25T12:00:00Z");
        var exactly = Sample(now.AddHours(2), PatientAppointmentStatuses.Requested);
        var inside = Sample(now.AddHours(2).AddMinutes(-1), PatientAppointmentStatuses.Requested);
        var outside = Sample(now.AddHours(2).AddMinutes(1), PatientAppointmentStatuses.Requested);

        PatientAppointmentStatuses.CanCancel(exactly, now).Should().BeTrue();
        PatientAppointmentStatuses.CanReschedule(exactly, now).Should().BeTrue();
        PatientAppointmentStatuses.CanCancel(inside, now).Should().BeFalse();
        PatientAppointmentStatuses.CanCancel(outside, now).Should().BeTrue();
    }

    [Fact]
    public void DisplayStatus_Uses_Patient_Safe_Labels()
    {
        PatientAppointmentStatuses.DisplayStatus(PatientAppointmentStatuses.CancelledByPatient)
            .Should().Be("Cancelled by you");
        PatientAppointmentStatuses.DisplayStatus(PatientAppointmentStatuses.CheckedIn)
            .Should().Be("Checked in");
    }

    [Fact]
    public void AppointmentTimeDisplay_Prefers_Clinic_Timezone_Label()
    {
        var appointment = Sample(
            DateTimeOffset.Parse("2026-07-26T21:30:00Z"),
            PatientAppointmentStatuses.Requested,
            timeZoneId: "UTC");

        var text = AppointmentTimeDisplay.FormatShort(appointment);
        text.Should().Contain("2026-07-26");
        text.Should().Contain("UTC");
    }

    [Fact]
    public void AppointmentTimeDisplay_Falls_Back_To_Device_Local_Label()
    {
        var appointment = Sample(
            DateTimeOffset.Parse("2026-07-26T21:30:00Z"),
            PatientAppointmentStatuses.Requested,
            timeZoneId: null);

        AppointmentTimeDisplay.FormatRange(appointment).Should().Contain("device local");
    }

    [Fact]
    public async Task CancelAsync_Sends_ExpectedVersion_And_Maps_Cutoff()
    {
        var api = new AppointmentFakeApi
        {
            CancelResult = ApiResult<AppointmentResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Conflict,
                ErrorCode = AppointmentErrorCodes.PatientMutationCutoff,
            }),
        };
        var sut = CreateSut(api);
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var result = await sut.CancelAsync(id, expectedVersion: 7);
        result.IsSuccess.Should().BeFalse();
        api.LastCancelVersion.Should().Be(7);
        api.CancelCallCount.Should().Be(1);
        sut.MapMutationError(result.Error!).Should().Contain("two hours");
        sut.MapMutationError(result.Error!).Should().Contain("contact the clinic");
    }

    [Fact]
    public async Task CancelAsync_Success_Returns_CancelledByPatient()
    {
        var id = Guid.NewGuid();
        var api = new AppointmentFakeApi
        {
            CancelResult = ApiResult<AppointmentResponse>.Success(
                Sample(
                    DateTimeOffset.UtcNow.AddDays(1),
                    PatientAppointmentStatuses.CancelledByPatient,
                    id: id,
                    version: 2)),
        };

        var result = await CreateSut(api).CancelAsync(id, expectedVersion: 1);
        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(PatientAppointmentStatuses.CancelledByPatient);
        result.Value.Id.Should().Be(id);
    }

    [Fact]
    public async Task RescheduleAsync_Preserves_Appointment_Identity()
    {
        var id = Guid.NewGuid();
        var newStart = DateTimeOffset.UtcNow.AddDays(3);
        var api = new AppointmentFakeApi
        {
            RescheduleResult = ApiResult<AppointmentResponse>.Success(
                Sample(newStart, PatientAppointmentStatuses.Requested, id: id, version: 3)),
        };

        var result = await CreateSut(api).RescheduleAsync(
            id,
            new RescheduleAppointmentRequest
            {
                DoctorStaffMemberId = Guid.NewGuid(),
                AppointmentDateUtc = newStart,
                DurationMinutes = 30,
                ExpectedVersion = 2,
            });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(id);
        api.RescheduleCallCount.Should().Be(1);
        api.LastRescheduleVersion.Should().Be(2);
    }

    [Fact]
    public async Task ListAsync_Passes_Query_Through()
    {
        var api = new AppointmentFakeApi
        {
            ListResult = ApiResult<PagedResponse<AppointmentResponse>>.Success(
                new PagedResponse<AppointmentResponse>
                {
                    Items = [Sample(DateTimeOffset.UtcNow.AddDays(1), PatientAppointmentStatuses.Requested)],
                    Page = 1,
                    PageSize = 20,
                    TotalCount = 1,
                    TotalPages = 1,
                }),
        };

        var query = new AppointmentListQuery { Page = 2, SortDirection = "desc" };
        var result = await CreateSut(api).ListAsync(query);
        result.IsSuccess.Should().BeTrue();
        api.LastListQuery.Should().BeSameAs(query);
    }

    [Fact]
    public async Task GetAsync_Maps_Unavailable()
    {
        var api = new AppointmentFakeApi
        {
            GetResult = ApiResult<AppointmentResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.NotFound,
                ErrorCode = AppointmentErrorCodes.NotFoundOrDenied,
            }),
        };

        var result = await CreateSut(api).GetAsync(Guid.NewGuid());
        result.IsSuccess.Should().BeFalse();
        CreateSut(api).MapMutationError(result.Error!).Should().Be("This appointment is not available.");
    }

    [Theory]
    [InlineData("/appointments", true)]
    [InlineData("/appointments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", true)]
    [InlineData("/appointments/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/reschedule", true)]
    [InlineData("/sign-in", false)]
    public void Appointment_Routes_Require_Authentication(string path, bool required)
    {
        PatientRoutes.RequiresAuthentication(path).Should().Be(required);
    }

    [Fact]
    public void AppointmentDetails_And_Reschedule_Routes_Are_Stable()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        PatientRoutes.AppointmentDetails(id).Should().Be($"/appointments/{id:D}");
        PatientRoutes.AppointmentReschedule(id).Should().Be($"/appointments/{id:D}/reschedule");
    }

    [Fact]
    public void Discovery_Clear_On_Logout_Clears_Reschedule_Slot_State()
    {
        var discovery = new DiscoveryStateService();
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

        discovery.Current.HasSlot.Should().BeTrue();
        discovery.Clear();
        discovery.Current.HasSlot.Should().BeFalse();
        discovery.Current.ClinicCode.Should().BeNull();
    }

    [Fact]
    public void BuildAppointmentListPath_Includes_Paging_And_Bounds()
    {
        var path = HealthCareApiClient.BuildAppointmentListPath(new AppointmentListQuery
        {
            Page = 2,
            PageSize = 20,
            SortBy = "appointmentDateUtc",
            SortDirection = "asc",
            FromUtc = DateTimeOffset.Parse("2026-07-25T00:00:00Z"),
        });

        path.Should().StartWith("api/v1/patients/me/appointments?");
        path.Should().Contain("page=2");
        path.Should().Contain("pageSize=20");
        path.Should().Contain("sortDirection=asc");
        path.Should().Contain("fromUtc=");
    }

    [Fact]
    public void MapMutationError_Covers_Concurrency_And_Slot_Conflict()
    {
        var sut = CreateSut(new AppointmentFakeApi());
        sut.MapMutationError(new ApiProblem
            {
                ErrorCode = AppointmentErrorCodes.ConcurrencyConflict,
                Title = "x",
            })
            .Should().Contain("changed");
        sut.MapMutationError(new ApiProblem
            {
                ErrorCode = AppointmentErrorCodes.SlotConflict,
                Title = "x",
            })
            .Should().Contain("no longer available");
    }

    private static PatientAppointmentService CreateSut(AppointmentFakeApi api) =>
        new(api, NullLogger<PatientAppointmentService>.Instance);

    private static AppointmentResponse Sample(
        DateTimeOffset startUtc,
        string status,
        int durationMinutes = 30,
        Guid? id = null,
        int version = 1,
        string? timeZoneId = "UTC") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            AppointmentDateUtc = startUtc,
            DurationMinutes = durationMinutes,
            EndsAtUtc = startUtc.AddMinutes(durationMinutes),
            Status = status,
            Version = version,
            DoctorStaffMemberId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ClinicName = "Dev Clinic A",
            ClinicSlug = "dev-clinic-a",
            DoctorDisplayName = "Dr Example",
            ClinicTimeZoneId = timeZoneId,
            Reason = "Checkup",
        };

    private sealed class AppointmentFakeApi : IHealthCareApiClient
    {
        public int CancelCallCount { get; private set; }

        public int RescheduleCallCount { get; private set; }

        public int? LastCancelVersion { get; private set; }

        public int? LastRescheduleVersion { get; private set; }

        public AppointmentListQuery? LastListQuery { get; private set; }

        public ApiResult<PagedResponse<AppointmentResponse>> ListResult { get; set; } =
            ApiResult<PagedResponse<AppointmentResponse>>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

        public ApiResult<AppointmentResponse> GetResult { get; set; } =
            ApiResult<AppointmentResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

        public ApiResult<AppointmentResponse> CancelResult { get; set; } =
            ApiResult<AppointmentResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

        public ApiResult<AppointmentResponse> RescheduleResult { get; set; } =
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
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ApiResult<PagedResponse<AppointmentResponse>>> ListPatientAppointmentsAsync(
            AppointmentListQuery query,
            CancellationToken cancellationToken = default)
        {
            LastListQuery = query;
            return Task.FromResult(ListResult);
        }

        public Task<ApiResult<AppointmentResponse>> GetAppointmentAsync(
            Guid appointmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GetResult);

        public Task<ApiResult<AppointmentResponse>> CancelAppointmentAsync(
            Guid appointmentId,
            AppointmentActionRequest request,
            CancellationToken cancellationToken = default)
        {
            CancelCallCount++;
            LastCancelVersion = request.ExpectedVersion;
            return Task.FromResult(CancelResult);
        }

        public Task<ApiResult<AppointmentResponse>> RescheduleAppointmentAsync(
            Guid appointmentId,
            RescheduleAppointmentRequest request,
            CancellationToken cancellationToken = default)
        {
            RescheduleCallCount++;
            LastRescheduleVersion = request.ExpectedVersion;
            return Task.FromResult(RescheduleResult);
        }
    }
}
