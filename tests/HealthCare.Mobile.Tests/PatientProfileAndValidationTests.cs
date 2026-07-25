using FluentAssertions;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Mobile.Core.Api;
using HealthCare.Mobile.Core.Authentication;
using HealthCare.Mobile.Core.Navigation;
using HealthCare.Mobile.Core.Patients;
using HealthCare.Mobile.Core.Validation;
using HealthCare.Mobile.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Mobile.Tests;

public sealed class PatientProfileAndValidationTests
{
    [Fact]
    public void ValidateRegistration_Requires_Password_Policy()
    {
        var errors = PatientFormValidators.ValidateRegistration(new PatientRegisterRequest
        {
            Email = "a@b.c",
            Password = "short",
            ConfirmPassword = "short",
            FirstName = "A",
            LastName = "B",
        });

        errors.Should().ContainKey("Password");
    }

    [Fact]
    public void ValidateRegistration_Accepts_Valid_Payload()
    {
        var errors = PatientFormValidators.ValidateRegistration(new PatientRegisterRequest
        {
            Email = "patient@example.com",
            Password = "Password1!",
            ConfirmPassword = "Password1!",
            FirstName = "Pat",
            LastName = "Ent",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30)),
            PhoneNumber = "+15551212",
        });

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateSignIn_Requires_Email_And_Password()
    {
        PatientFormValidators.ValidateSignIn("", "").Should().HaveCount(2);
        PatientFormValidators.ValidateSignIn("bad", "x").Should().ContainKey("Email");
    }

    [Fact]
    public void ValidateProfileEdit_Rejects_Future_Dob()
    {
        var errors = PatientFormValidators.ValidateProfileEdit(new PatientProfileEditModel
        {
            FirstName = "A",
            LastName = "B",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)),
        });

        errors.Should().ContainKey("DateOfBirth");
    }

    [Fact]
    public void ToUpdateRequest_Sets_ExpectedVersion_And_Fields()
    {
        var request = PatientFormValidators.ToUpdateRequest(
            new PatientProfileEditModel
            {
                FirstName = "A",
                LastName = "B",
                MobileNumber = "123",
            },
            expectedVersion: 4);

        request.ExpectedVersion.Should().Be(4);
        request.FirstNameSpecified.Should().BeTrue();
        request.LastNameSpecified.Should().BeTrue();
        request.MobileNumber.Should().Be("123");
    }

    [Fact]
    public void ApiProblem_Maps_Profile_Concurrency_Message()
    {
        var problem = new ApiProblem
        {
            Kind = ApiErrorKind.Conflict,
            ErrorCode = PatientErrorCodes.ConcurrencyConflict,
            Title = "Conflict",
        };

        problem.UserMessage.Should().Contain("updated elsewhere");
        problem.UserMessage.Should().NotContain("Version");
    }

    [Fact]
    public async Task PatientProfileService_Get_Returns_Profile()
    {
        var profile = new PatientProfileResponse
        {
            Id = Guid.NewGuid(),
            FirstName = "A",
            LastName = "B",
            Version = 2,
            IsActive = true,
        };

        var api = new ProfileFakeApi
        {
            GetResult = ApiResult<PatientProfileResponse>.Success(profile),
        };
        var sut = new PatientProfileService(api);

        var result = await sut.GetAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value!.FirstName.Should().Be("A");
        result.Value.Version.Should().Be(2);
    }

    [Fact]
    public async Task PatientProfileService_Propagates_Conflict()
    {
        var api = new ProfileFakeApi
        {
            GetResult = ApiResult<PatientProfileResponse>.Success(new PatientProfileResponse
            {
                Id = Guid.NewGuid(),
                FirstName = "A",
                LastName = "B",
                Version = 1,
                IsActive = true,
            }),
            UpdateResult = ApiResult<PatientProfileResponse>.Failure(new ApiProblem
            {
                Kind = ApiErrorKind.Conflict,
                ErrorCode = PatientErrorCodes.ConcurrencyConflict,
            }),
        };
        var sut = new PatientProfileService(api);

        var updated = await sut.UpdateAsync(new UpdatePatientProfileRequest { ExpectedVersion = 1 });
        updated.IsSuccess.Should().BeFalse();
        updated.Error!.ErrorCode.Should().Be(PatientErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public void PatientIdentityRules_Rejects_Staff_Membership()
    {
        var user = new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Roles = ["PATIENT"],
            PatientId = Guid.NewGuid(),
            HasLinkedPatient = true,
            HasActiveStaffMembership = true,
            Permissions = [],
        };

        PatientIdentityRules.IsEligiblePatientAccount(user).Should().BeFalse();
    }

    [Theory]
    [InlineData("/profile", true)]
    [InlineData("/profile/edit", true)]
    [InlineData("/home", true)]
    [InlineData("/sign-in", false)]
    [InlineData("/confirm-email", false)]
    [InlineData("/registration-complete", false)]
    public void RequiresAuthentication_Includes_Profile_Routes(string path, bool required)
    {
        PatientRoutes.RequiresAuthentication(path).Should().Be(required);
    }

    [Theory]
    [InlineData("/sign-in", true)]
    [InlineData("/register", true)]
    [InlineData("/confirm-email", true)]
    [InlineData("/home", false)]
    public void IsGuestOnly_Matches_Auth_Screens(string path, bool guest)
    {
        PatientRoutes.IsGuestOnly(path).Should().Be(guest);
    }

    private sealed class ProfileFakeApi : IHealthCareApiClient
    {
        public ApiResult<PatientProfileResponse> GetResult { get; set; } =
            ApiResult<PatientProfileResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

        public ApiResult<PatientProfileResponse> UpdateResult { get; set; } =
            ApiResult<PatientProfileResponse>.Failure(new ApiProblem { Kind = ApiErrorKind.Unknown });

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
            Task.FromResult(GetResult);

        public Task<ApiResult<PatientProfileResponse>> UpdatePatientProfileAsync(
            UpdatePatientProfileRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UpdateResult);

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
    }
}
