using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Organizations;
using HealthCare.Web.Auth;
using HealthCare.Web.Governance;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminAuditUsageUiTests
{
    [Fact]
    public async Task Organization_Admin_Can_View_Audit_And_Usage()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.OrganizationAuditLogsRead,
                WebPermissions.OrganizationUsageRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        GovernancePermissionRules.CanViewAuditLogs(state).Should().BeTrue();
        GovernancePermissionRules.CanViewUsage(state).Should().BeTrue();
        GovernancePermissionRules.CanViewAny(state).Should().BeTrue();
    }

    [Fact]
    public async Task Audit_Only_Does_Not_Grant_Usage()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "viewer@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.OrganizationAuditLogsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        GovernancePermissionRules.CanViewAuditLogs(state).Should().BeTrue();
        GovernancePermissionRules.CanViewUsage(state).Should().BeFalse();
        GovernancePermissionRules.CanViewAny(state).Should().BeTrue();
    }

    [Fact]
    public void Result_And_Limit_Tones_Are_Mapped()
    {
        OrganizationAuditPresentation.ResultTone("succeeded").Should().Be(Design.StatusTone.Success);
        OrganizationAuditPresentation.ResultTone("failed").Should().Be(Design.StatusTone.Error);
        OrganizationAuditPresentation.TruncateId(Guid.Parse("11111111-1111-1111-1111-111111111111"))
            .Should().StartWith("11111111");
        OrganizationAuditPresentation.TruncateCorrelation("abcdefghijklmnopqrstuvwxyz")
            .Should().EndWith("…");

        OrganizationUsagePresentation.LimitTone(reached: true, warning: false).Should().Be(Design.StatusTone.Error);
        OrganizationUsagePresentation.LimitTone(reached: false, warning: true).Should().Be(Design.StatusTone.Warning);
        OrganizationUsagePresentation.CapacityPercent(5, 10).Should().Be(50);
        OrganizationUsagePresentation.CapacityPercent(1, 0).Should().Be(0);
    }

    [Fact]
    public void Problem_Messages_Cover_Access_And_Date_Range()
    {
        var denied = new ApiProblemException(403, "Denied", "raw", OrganizationAuditLogErrorCodes.AccessDenied);
        OrganizationAuditProblemMessages.ToUserMessage(denied).Should().Contain("permission");
        OrganizationAuditProblemMessages.ToUserMessage(denied).Should().NotContain("raw");

        var range = new ApiProblemException(400, "Range", null, OrganizationAuditLogErrorCodes.InvalidDateRange);
        OrganizationAuditProblemMessages.ToUserMessage(range).Should().Contain("93");

        var usageDenied = new ApiProblemException(403, "Denied", "raw", OrganizationUsageErrorCodes.AccessDenied);
        OrganizationUsageProblemMessages.ToUserMessage(usageDenied).Should().Contain("permission");
        OrganizationUsageProblemMessages.ToUserMessage(usageDenied).Should().NotContain("raw");
    }

    [Fact]
    public void Audit_Item_Contract_Has_No_Unsafe_Fields()
    {
        typeof(OrganizationAuditLogItem).GetProperty("Password").Should().BeNull();
        typeof(OrganizationAuditLogItem).GetProperty("Token").Should().BeNull();
        typeof(OrganizationAuditLogItem).GetProperty("RequestBody").Should().BeNull();
        typeof(OrganizationAuditLogItem).GetProperty("StackTrace").Should().BeNull();
        typeof(OrganizationAuditLogItem).GetProperty("Metadata").Should().BeNull();
        typeof(OrganizationAuditLogItem).GetProperty("Payload").Should().BeNull();
    }

    [Fact]
    public void Audit_And_Usage_Pages_Use_Typed_Clients()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var auditPage = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "AuditLogs.razor"));
        var usagePage = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Usage.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));

        auditPage.Should().Contain("IOrganizationAuditLogApiClient");
        auditPage.Should().Contain("IClinicWorkingContext");
        auditPage.Should().Contain("Correlation lookup");
        auditPage.Should().Contain("immutable");
        auditPage.Should().Contain("Retention");
        auditPage.Should().NotContain("@inject HttpClient");
        auditPage.Should().NotContain("RequestBody");
        auditPage.Should().NotContain("StackTrace");

        usagePage.Should().Contain("IOrganizationUsageApiClient");
        usagePage.Should().Contain("IClinicWorkingContext");
        usagePage.Should().Contain("Remaining");
        usagePage.Should().Contain("Max clinics");
        usagePage.Should().Contain("Max staff");
        usagePage.Should().Contain("cannot be changed");
        usagePage.Should().Contain("Snapshot");
        usagePage.Should().NotContain("@inject HttpClient");
        usagePage.Should().NotContain("checkout");
        usagePage.Should().NotContain("billing");

        layout.Should().Contain("/audit-logs");
        layout.Should().Contain("/usage");
        layout.Should().Contain("Governance");
        layout.Should().Contain("GovernancePermissionRules.CanViewAny");
    }

    [Fact]
    public void Audit_And_Usage_Clients_Hit_Organization_Endpoints()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "OrganizationGovernanceApiClients.cs")));

        source.Should().Contain("api/v1/organization/audit-logs");
        source.Should().Contain("by-correlation/");
        source.Should().Contain("api/v1/organization/usage");
        source.Should().Contain("platformAdminBypass");
    }
}
