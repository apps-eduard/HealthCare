using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.ClinicAudit;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class ClinicAdminAuditUiTests
{
    [Fact]
    public async Task Clinic_Admin_Sees_Audit_Permission_And_Navigation()
    {
        var state = await ClinicAdminStateAsync();
        ClinicAuditPermissionRules.CanView(state).Should().BeTrue();
        state.Has(WebPermissions.ClinicAuditLogsRead).Should().BeTrue();
        state.Has(WebPermissions.OrganizationAuditLogsRead).Should().BeFalse();

        var webRoot = WebRoot();
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        layout.Should().Contain("/clinic/audit-logs");
        layout.Should().Contain("ClinicAuditPermissionRules.CanView");
        layout.Should().Contain("clinic-audit");
        layout.Should().Contain("/audit-logs");
    }

    [Fact]
    public async Task Organization_Admin_And_Patient_Do_Not_See_Clinic_Audit()
    {
        var org = new PermissionState();
        await org.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.OrganizationAuditLogsRead, WebPermissions.ClinicsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });
        ClinicAuditPermissionRules.CanView(org).Should().BeFalse();

        var patient = new PermissionState();
        await patient.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "patient@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [],
            HasActiveStaffMembership = false,
        });
        ClinicAuditPermissionRules.CanView(patient).Should().BeFalse();
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
            Permissions = [WebPermissions.ClinicAuditLogsRead],
            HasActiveStaffMembership = false,
        });

        ClinicAuditPermissionRules.CanView(state).Should().BeTrue();
        state.IsPlatformAdmin.Should().BeTrue();
        ClinicAuditPageCopy.Subtitle(state).Should().Contain("selected clinic");
    }

    [Fact]
    public void Page_Renders_Filters_And_Safe_States_Without_Picker_Or_Export()
    {
        var page = File.ReadAllText(Path.Combine(WebRoot(), "Components", "Pages", "ClinicAuditLogs.razor"));
        page.Should().Contain("@page \"/clinic/audit-logs\"");
        page.Should().Contain("Clinic Audit Logs");
        page.Should().Contain("IClinicAuditLogApiClient");
        page.Should().Contain("clinic-audit-clinic-caption");
        page.Should().Contain("ClinicAuditPageCopy.MaxInclusiveDays");
        page.Should().Contain("From date must be on or before To date");
        page.Should().Contain("Date range cannot exceed");
        page.Should().Contain("clinic-audit-category");
        page.Should().Contain("clinic-audit-action");
        page.Should().Contain("clinic-audit-result");
        page.Should().Contain("PageLoading");
        page.Should().Contain("EmptyState");
        page.Should().Contain("ClinicAuditProblemMessages");
        page.Should().Contain("Select a clinic in the platform banner");
        page.Should().Contain("Enable explicit platform bypass");
        page.Should().Contain("Details");
        page.Should().Contain("Raw metadata, passwords, tokens, and clinical content are not available.");
        page.Should().NotContain("ClinicPicker");
        page.Should().NotContain("Export CSV");
        page.Should().NotContain("Export PDF");
        page.Should().NotContain("@inject HttpClient");
        page.Should().NotContain("row.Password");
        page.Should().NotContain("ev.Metadata");
        page.Should().NotContain("row.Metadata");
        page.Should().NotContain("MedicalNote");
        page.Should().NotContain("/organization/settings");
    }

    [Fact]
    public void Presentation_And_Problem_Messages_Are_Safe()
    {
        ClinicAuditPageCopy.MaxInclusiveDays.Should().Be(93);
        ClinicAuditProblemMessages.ToUserMessage(
                new ApiProblemException(400, "Bad", "raw", ClinicAuditLogErrorCodes.InvalidDateRange))
            .Should().Contain("93").And.NotContain("raw");
        ClinicAuditProblemMessages.ToUserMessage(
                new ApiProblemException(400, "Bad", null, ClinicAuditLogErrorCodes.ClinicScopeRequired))
            .Should().Contain("clinic");
        ClinicAuditProblemMessages.ToUserMessage(
                new ApiProblemException(403, "Denied", "stack", ClinicAuditLogErrorCodes.AccessDenied))
            .Should().Contain("permission").And.NotContain("stack");
        ClinicAuditProblemMessages.ToUserMessage(
                new ApiProblemException(404, "Missing", null, ClinicAuditLogErrorCodes.ClinicNotFound))
            .Should().Contain("not found");
    }

    [Fact]
    public void Typed_Client_And_Program_Registration_Exist()
    {
        var program = File.ReadAllText(Path.Combine(WebRoot(), "Program.cs"));
        program.Should().Contain("IClinicAuditLogApiClient");

        var source = File.ReadAllText(Path.Combine(WebRoot(), "Services", "ClinicAuditLogApiClient.cs"));
        source.Should().Contain("api/v1/clinic/audit-logs");
        source.Should().Contain("platformAdminBypass=true");
        source.Should().NotContain("export");

        typeof(IClinicAuditLogApiClient).GetMethods()
            .Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Export", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Organization_Audit_Experience_Remains_Unchanged()
    {
        var page = File.ReadAllText(Path.Combine(WebRoot(), "Components", "Pages", "AuditLogs.razor"));
        page.Should().Contain("@page \"/audit-logs\"");
        page.Should().Contain("OrganizationAuditLogsRead");
        page.Should().NotContain("clinic_audit_logs.read");
        page.Should().NotContain("/clinic/audit-logs");
    }

    [Fact]
    public void Contracts_Remain_Safe_Projection()
    {
        typeof(ClinicAuditLogItem).GetProperty("Metadata").Should().BeNull();
        typeof(ClinicAuditLogItem).GetProperty("Password").Should().BeNull();
        typeof(ClinicAuditLogItem).GetProperty("Token").Should().BeNull();
        typeof(ClinicAuditLogItem).GetProperty("MedicalNote").Should().BeNull();
        typeof(ClinicAuditLogDetailResponse).GetProperty("Metadata").Should().BeNull();
        ClinicAuditActions.All.Should().Contain("clinic_profile_update");
        ClinicAuditActions.ToSummary("clinic_profile_update").Should().Be("Clinic profile updated");
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
                WebPermissions.ClinicAuditLogsRead,
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
