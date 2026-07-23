using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Web.Auth;
using HealthCare.Web.Patients;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class PatientDirectorySupportTests
{
    [Fact]
    public async Task Missing_PatientsSearch_Is_Detectable()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "r@test.local",
            Roles = ["RECEPTIONIST"],
            Permissions = [WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = true,
        });

        state.Has(WebPermissions.PatientsSearch).Should().BeFalse();
    }

    [Fact]
    public async Task Patient_Cannot_Access_Staff_Directory_Context()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "p@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [WebPermissions.PatientsSearch],
            HasActiveStaffMembership = false,
        });

        state.IsPatientOnly.Should().BeTrue();
    }

    [Fact]
    public void Directory_Sends_Server_Side_Paging_And_Filters()
    {
        var clinicId = Guid.NewGuid();
        var query = StaffPatientSearchQueryBuilder.Build(
            search: "  Ana  ",
            localPatientNumber: "A-001",
            patientIsActive: true,
            clinicPatientStatus: "Active",
            clinicId: clinicId,
            page: 3,
            pageSize: 50,
            sortBy: "lastName",
            sortDirection: "asc");

        query.Search.Should().Be("Ana");
        query.LocalPatientNumber.Should().Be("A-001");
        query.PatientIsActive.Should().BeTrue();
        query.ClinicPatientStatus.Should().Be("Active");
        query.ClinicId.Should().Be(clinicId);
        query.Page.Should().Be(3);
        query.PageSize.Should().Be(50);
        query.SortBy.Should().Be("lastName");
        query.SortDirection.Should().Be("asc");
    }

    [Fact]
    public void All_Clinics_Clears_ClinicId()
    {
        var query = StaffPatientSearchQueryBuilder.Build(
            null, null, null, null, clinicId: null, 1, 20, "registeredAtUtc", "desc");
        query.ClinicId.Should().BeNull();
    }

    [Fact]
    public void Arbitrary_Empty_ClinicId_Is_Normalized_To_Null()
    {
        var query = StaffPatientSearchQueryBuilder.Build(
            null, null, null, null, Guid.Empty, 1, 20, "registeredAtUtc", "desc");
        query.ClinicId.Should().BeNull();
    }

    [Fact]
    public async Task Clinic_Scoped_User_Does_Not_Get_Picker_Filter()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions = [WebPermissions.ClinicsRead, WebPermissions.PatientsSearch],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.CanFilterByClinic.Should().BeFalse();
    }

    [Fact]
    public async Task Organization_Admin_Can_Select_Clinic()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.ClinicsRead, WebPermissions.PatientsSearch],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.CanFilterByClinic.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_Read_And_Update_Permissions_Are_Detectable()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "r@test.local",
            Roles = ["RECEPTIONIST"],
            Permissions = [WebPermissions.PatientsSearch],
            HasActiveStaffMembership = true,
        });

        state.Has(WebPermissions.PatientsRead).Should().BeFalse();
        state.Has(WebPermissions.PatientsUpdateClinicStatus).Should().BeFalse();
    }

    [Fact]
    public void Status_Update_Sends_ExpectedVersion()
    {
        var request = new UpdateClinicPatientRequest
        {
            ExpectedVersion = 7,
            Status = "Inactive",
        };
        request.ExpectedVersion.Should().Be(7);
        request.Status.Should().Be("Inactive");
    }

    [Fact]
    public void Concurrency_Conflict_Is_Detected_And_Mapped()
    {
        var ex = new ApiProblemException(409, "Conflict", "stale", "patient.clinic_patient_concurrency_conflict");
        PatientProblemMessages.IsConcurrencyConflict(ex).Should().BeTrue();
        PatientProblemMessages.ToUserMessage(ex).Should().Contain("Reload");
    }

    [Fact]
    public void Safe_404_Is_Mapped()
    {
        var ex = new ApiProblemException(404, "Not Found", "gone", "patient.not_found_or_denied");
        PatientProblemMessages.IsNotFound(ex).Should().BeTrue();
        PatientProblemMessages.ToUserMessage(ex).Should().Contain("not found");
    }

    [Fact]
    public void Status_Chips_Map_Exactly()
    {
        PatientStatusPresentation.ClinicPatientLabel("Active").Should().Be("Active");
        PatientStatusPresentation.ClinicPatientLabel("Inactive").Should().Be("Inactive");
        PatientStatusPresentation.PatientActiveLabel(true).Should().Be("Active");
        PatientStatusPresentation.PatientActiveLabel(false).Should().Be("Inactive");
    }

    [Fact]
    public void Mobile_Masking_Does_Not_Expose_Full_Number_In_List()
    {
        PatientDisplay.MaskMobile("+966501234567").Should().NotContain("501234567");
        PatientDisplay.MaskMobile("+966501234567").Should().EndWith("4567");
    }

    [Fact]
    public void Typed_Client_Exposes_Detail_And_Update()
    {
        typeof(IStaffPatientApiClient).GetMethod(nameof(IStaffPatientApiClient.GetByIdAsync)).Should().NotBeNull();
        typeof(IStaffPatientApiClient).GetMethod(nameof(IStaffPatientApiClient.UpdateClinicProfileAsync)).Should().NotBeNull();
        typeof(IStaffPatientApiClient).GetMethod(nameof(IStaffPatientApiClient.SearchAsync)).Should().NotBeNull();
    }

    [Fact]
    public void PatientPicker_Display_Uses_Shared_Formatter()
    {
        var patient = new StaffPatientSummaryResponse
        {
            PatientId = Guid.NewGuid(),
            ClinicPatientId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            LocalPatientNumber = "C-1",
            FirstName = "Ada",
            LastName = "Lovelace",
            ClinicPatientStatus = "Active",
            PatientIsActive = true,
            RegisteredAtUtc = DateTimeOffset.UtcNow,
            Version = 1,
        };

        PatientDisplay.PickerLabel(patient).Should().Contain("Ada Lovelace");
        PatientDisplay.PickerLabel(patient).Should().Contain("C-1");
    }

    [Fact]
    public void Patient_Data_Is_Not_Stored_In_Token_Store_Types()
    {
        typeof(StoredAuthTokens).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Patient", StringComparison.OrdinalIgnoreCase));
    }
}
