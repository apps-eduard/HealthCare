using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.ClinicReports;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminReportsUiTests
{
    [Fact]
    public async Task Clinic_Admin_Sees_Reports_Permission_And_Navigation()
    {
        var state = await ClinicAdminStateAsync();
        ClinicReportsPermissionRules.CanView(state).Should().BeTrue();
        state.Has(WebPermissions.ClinicReportsRead).Should().BeTrue();
        state.Has(WebPermissions.OrganizationReportsRead).Should().BeFalse();
        state.Has("clinic_audit_logs.read").Should().BeFalse();

        var webRoot = WebRoot();
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        layout.Should().Contain("/clinic/reports");
        layout.Should().Contain("ClinicReportsPermissionRules.CanView");
        layout.Should().Contain("clinic-reports");
        layout.Should().NotContain("/clinic/audit");
        layout.Should().NotContain("clinic_audit_logs");
    }

    [Fact]
    public async Task Organization_Admin_And_Patient_Do_Not_See_Clinic_Reports()
    {
        var org = new PermissionState();
        await org.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.OrganizationReportsRead, WebPermissions.ClinicsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        ClinicReportsPermissionRules.CanView(org).Should().BeFalse();

        var patient = new PermissionState();
        await patient.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "patient@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [],
            HasActiveStaffMembership = false,
        });
        ClinicReportsPermissionRules.CanView(patient).Should().BeFalse();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Clinic_Context()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions = [WebPermissions.ClinicReportsRead],
            HasActiveStaffMembership = false,
        });

        ClinicReportsPermissionRules.CanView(state).Should().BeTrue();
        state.IsPlatformAdmin.Should().BeTrue();
    }

    [Fact]
    public void Page_Renders_Filters_Tabs_And_Safe_States_Without_Export()
    {
        var page = File.ReadAllText(Path.Combine(WebRoot(), "Components", "Pages", "ClinicReports.razor"));
        page.Should().Contain("@page \"/clinic/reports\"");
        page.Should().Contain("Clinic Reports");
        page.Should().Contain("IClinicReportsApiClient");
        page.Should().Contain("clinic-reports-clinic-caption");
        page.Should().Contain("ClinicReportsPageCopy.MaxInclusiveDays");
        page.Should().Contain("From date must be on or before To date");
        page.Should().Contain("Date range cannot exceed");
        page.Should().Contain("ClinicReportViewKeys.Appointments");
        page.Should().Contain("ClinicReportViewKeys.Doctors");
        page.Should().Contain("ClinicReportViewKeys.Patients");
        page.Should().Contain("ClinicReportViewKeys.Operations");
        page.Should().Contain("PageLoading");
        page.Should().Contain("EmptyState");
        page.Should().Contain("ClinicReportProblemMessages");
        page.Should().Contain("Select a clinic in the platform banner");
        page.Should().Contain("Enable explicit platform bypass");
        page.Should().NotContain("ClinicPicker");
        page.Should().NotContain("Export CSV");
        page.Should().NotContain("Export PDF");
        page.Should().NotContain("Schedule report");
        page.Should().NotContain("Email report");
        page.Should().NotContain("Print report");
        page.Should().NotContain("@inject HttpClient");
        page.Should().NotContain("PatientName");
        page.Should().NotContain("billing");
        page.Should().NotContain("MaxClinics");
        page.Should().NotContain("MedicalNote");
        page.Should().NotContain("/organization/settings");
    }

    [Fact]
    public void Presentation_And_Problem_Messages_Are_Safe()
    {
        ClinicReportsPageCopy.MaxInclusiveDays.Should().Be(93);
        ClinicReportProblemMessages.ToUserMessage(
                new ApiProblemException(400, "Bad", "raw", ClinicReportErrorCodes.InvalidDateRange))
            .Should().Contain("93").And.NotContain("raw");
        ClinicReportProblemMessages.ToUserMessage(
                new ApiProblemException(400, "Bad", null, ClinicReportErrorCodes.ClinicScopeRequired))
            .Should().Contain("clinic");
        ClinicReportProblemMessages.ToUserMessage(
                new ApiProblemException(403, "Denied", "stack", ClinicReportErrorCodes.AccessDenied))
            .Should().Contain("permission").And.NotContain("stack");
        ClinicReportProblemMessages.ToUserMessage(
                new ApiProblemException(404, "Missing", null, ClinicReportErrorCodes.ClinicNotFound))
            .Should().Contain("not found");
    }

    [Fact]
    public void Typed_Client_And_Program_Registration_Exist()
    {
        var program = File.ReadAllText(Path.Combine(WebRoot(), "Program.cs"));
        program.Should().Contain("IClinicReportsApiClient");

        var source = File.ReadAllText(Path.Combine(WebRoot(), "Services", "ClinicReportsApiClient.cs"));
        source.Should().Contain("api/v1/clinic/reports/appointments");
        source.Should().Contain("api/v1/clinic/reports/doctors");
        source.Should().Contain("api/v1/clinic/reports/patients");
        source.Should().Contain("api/v1/clinic/reports/reminders");
        source.Should().Contain("platformAdminBypass=true");
        source.Should().NotContain("export");

        typeof(IClinicReportsApiClient).GetMethods()
            .Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Export", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Organization_Reports_Experience_Remains_Unchanged()
    {
        var page = File.ReadAllText(Path.Combine(WebRoot(), "Components", "Pages", "Reports.razor"));
        page.Should().Contain("@page \"/reports\"");
        page.Should().Contain("Organization Reports");
        page.Should().Contain("Export CSV");
        page.Should().Contain("OrganizationReportsRead");
        page.Should().NotContain("clinic_reports.read");
    }

    [Fact]
    public void Contracts_Remain_Aggregate_Only()
    {
        typeof(ClinicAppointmentReportResponse).GetProperty("PatientName").Should().BeNull();
        typeof(ClinicDoctorAppointmentRow).GetProperty("PatientName").Should().BeNull();
        typeof(ClinicPatientEnrollmentReportResponse).GetProperty("Patients").Should().BeNull();
        typeof(ClinicOperationsReportResponse).GetProperty("MessageBody").Should().BeNull();
        typeof(ClinicOperationsReportResponse).GetProperty("HangfireQueues").Should().BeNull();
    }

    private static async Task<PermissionState> ClinicAdminStateAsync()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "clinicadmin@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions =
            [
                WebPermissions.ClinicReportsRead,
                WebPermissions.ClinicDashboardRead,
                WebPermissions.ClinicProfileRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        return state;
    }

    private static string WebRoot() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
}
