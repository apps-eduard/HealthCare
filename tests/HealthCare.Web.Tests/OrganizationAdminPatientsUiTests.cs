using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Patients;
using HealthCare.Web.Auth;
using HealthCare.Web.Patients;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminPatientsUiTests
{
    [Fact]
    public void Patient_Problem_Messages_Cover_Enrollment_And_Self_Scope()
    {
        var conflict = new ApiProblemException(
            409, "Conflict", null, PatientErrorCodes.ClinicPatientConcurrencyConflict);
        PatientProblemMessages.ToUserMessage(conflict).Should().Contain("Reload");

        var self = new ApiProblemException(403, "Denied", null, "authz.patient_self_scope_denied");
        PatientProblemMessages.ToUserMessage(self).Should().Contain("self-scope");

        var inactive = new ApiProblemException(409, "Inactive", null, PatientErrorCodes.ClinicInactive);
        PatientProblemMessages.ToUserMessage(inactive).Should().Contain("inactive");
    }

    [Fact]
    public async Task Patients_Read_And_Update_Permissions_Are_Distinct()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.PatientsSearch,
                WebPermissions.PatientsRead,
                WebPermissions.PatientsUpdateClinicStatus,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.PatientsSearch).Should().BeTrue();
        state.Has(WebPermissions.PatientsRead).Should().BeTrue();
        state.Has(WebPermissions.PatientsUpdateClinicStatus).Should().BeTrue();
        state.CanFilterByClinic.Should().BeTrue();
        state.IsOrganizationAdmin.Should().BeTrue();
        state.IsPatientOnly.Should().BeFalse();
    }

    [Fact]
    public void Patients_Page_Uses_Clinic_Context_Drawer_And_Typed_Client()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var patients = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Patients.razor"));
        var drawer = File.ReadAllText(Path.Combine(webRoot, "Components", "Patients", "PatientDetailDrawer.razor"));
        var enroll = File.ReadAllText(Path.Combine(webRoot, "Components", "Patients", "EnrollPatientDialog.razor"));
        var picker = File.ReadAllText(Path.Combine(webRoot, "Components", "Patients", "PatientPicker.razor"));

        patients.Should().Contain("IStaffPatientApiClient");
        patients.Should().Contain("IClinicWorkingContext");
        patients.Should().Contain("PatientDetailDrawer");
        patients.Should().Contain("WebPermissions.PatientsSearch");
        patients.Should().Contain("IsPatientOnly");
        patients.Should().NotContain("@inject HttpClient");

        drawer.Should().Contain("Enrollments");
        drawer.Should().Contain("ClinicId = _selectedEnrollment.ClinicId");
        drawer.Should().Contain("ExpectedVersion");
        drawer.Should().Contain("EnrollPatientDialog");

        enroll.Should().Contain("EnrollAsync");
        enroll.Should().Contain("AlreadyEnrolledClinicIds");

        picker.Should().Contain("LookupAsync");
        picker.Should().Contain("StaffPatientLookupRequest");
    }

    [Fact]
    public void Staff_Patient_Client_Includes_Lookup_Detail_ClinicId_And_Enroll()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "StaffPatientApiClient.cs")));

        source.Should().Contain("api/v1/staff/patients/lookup");
        source.Should().Contain("LookupAsync");
        source.Should().Contain("clinicId=");
        source.Should().Contain("/enroll");
        source.Should().Contain("EnrollAsync");
        source.Should().Contain("clinic-profile");
        source.Should().Contain("platformAdminBypass=true");
    }

    [Fact]
    public void Status_Update_Request_Can_Target_Clinic()
    {
        var clinicId = Guid.NewGuid();
        var request = new UpdateClinicPatientRequest
        {
            ExpectedVersion = 3,
            Status = "Inactive",
            ClinicId = clinicId,
        };

        request.ClinicId.Should().Be(clinicId);
        request.ExpectedVersion.Should().Be(3);
    }

    [Fact]
    public void Lookup_Item_Maps_To_Picker_Summary()
    {
        var item = new StaffPatientLookupItemResponse
        {
            PatientId = Guid.NewGuid(),
            ClinicPatientId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
            LocalPatientNumber = "P-1",
            FirstName = "Ada",
            LastName = "Lovelace",
            DateOfBirth = new DateOnly(1815, 12, 10),
        };

        var summary = PatientDisplay.ToSummary(item);
        summary.PatientId.Should().Be(item.PatientId);
        summary.ClinicPatientStatus.Should().Be("Active");
        summary.PatientIsActive.Should().BeTrue();
        PatientDisplay.PickerLabel(item).Should().Contain("Ada Lovelace");
        PatientDisplay.PickerLabel(item).Should().Contain("P-1");
    }
}
