using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Security;
using HealthCare.Web.Auth;
using HealthCare.Web.Security;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminSecurityUiTests
{
    [Fact]
    public async Task Organization_Admin_Can_View_And_Revoke()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.SecuritySessionsRead,
                WebPermissions.SecuritySessionsRevoke,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        OrganizationSecurityPermissionRules.CanView(state).Should().BeTrue();
        OrganizationSecurityPermissionRules.CanRevoke(state).Should().BeTrue();
    }

    [Fact]
    public async Task Read_Only_Cannot_Revoke()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "viewer@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.SecuritySessionsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        OrganizationSecurityPermissionRules.CanView(state).Should().BeTrue();
        OrganizationSecurityPermissionRules.CanRevoke(state).Should().BeFalse();
    }

    [Fact]
    public void Ip_And_User_Agent_Are_Masked()
    {
        OrganizationSecurityPresentation.MaskIp("203.0.113.45").Should().Be("203.0.*.*");
        OrganizationSecurityPresentation.MaskIp("2001:db8::1").Should().StartWith("2001:");
        OrganizationSecurityPresentation.MaskIp(null).Should().Be("—");
        OrganizationSecurityPresentation.UserAgentSummary(
                "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36")
            .Should().Be("Chrome");
        OrganizationSecurityPresentation.UserAgentSummary(null).Should().Be("—");
    }

    [Fact]
    public void Problem_Messages_Cover_Self_And_Last_Admin()
    {
        var self = new ApiProblemException(403, "Denied", "raw", OrganizationSecurityErrorCodes.SelfCompromiseDenied);
        OrganizationSecurityProblemMessages.ToUserMessage(self).Should().Contain("own account");
        OrganizationSecurityProblemMessages.ToUserMessage(self).Should().NotContain("raw");

        var last = new ApiProblemException(409, "Protected", null, OrganizationSecurityErrorCodes.LastAdminProtected);
        OrganizationSecurityProblemMessages.ToUserMessage(last).Should().Contain("last Organization Admin");
    }

    [Fact]
    public void Session_Contract_Has_No_Token_Fields()
    {
        typeof(OrganizationSecuritySessionItem).GetProperty("RefreshToken").Should().BeNull();
        typeof(OrganizationSecuritySessionItem).GetProperty("AccessToken").Should().BeNull();
        typeof(OrganizationSecuritySessionItem).GetProperty("TokenHash").Should().BeNull();
        typeof(OrganizationSecuritySessionItem).GetProperty("SecurityStamp").Should().BeNull();
    }

    [Fact]
    public void Security_Page_Uses_Typed_Client_And_Masks_Secrets()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Security.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));

        page.Should().Contain("IOrganizationSecurityApiClient");
        page.Should().Contain("IClinicWorkingContext");
        page.Should().Contain("MaskIp");
        page.Should().Contain("UserAgentSummary");
        page.Should().Contain("Session invalidation behavior");
        page.Should().Contain("access token may remain");
        page.Should().Contain("SecurityRevokeSessionsDialog");
        page.Should().Contain("SecurityCompromiseDialog");
        page.Should().NotContain("@inject HttpClient");
        page.Should().NotContain("RefreshToken");
        page.Should().NotContain("SecurityStamp");

        layout.Should().Contain("/security");
        layout.Should().Contain("Security");
        layout.Should().Contain("OrganizationSecurityPermissionRules.CanView");
    }

    [Fact]
    public void Security_Client_Exposes_Sessions_Revoke_Compromise_And_Summaries()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "OrganizationSecurityApiClient.cs")));

        source.Should().Contain("api/v1/organization/security/");
        source.Should().Contain("\"sessions\"");
        source.Should().Contain("sessions/revoke");
        source.Should().Contain("compromise-response");
        source.Should().Contain("failed-logins");
        source.Should().Contain("authorization-denials");
        source.Should().Contain("cross-clinic-attempts");
        source.Should().Contain("includeRevoked");
    }
}
