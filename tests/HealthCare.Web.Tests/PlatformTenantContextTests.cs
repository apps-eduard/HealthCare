using FluentAssertions;
using HealthCare.Web.Auth;
using HealthCare.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.Web.Tests;

public sealed class PlatformTenantContextTests
{
    [Fact]
    public void Selecting_Organization_Clears_Clinic()
    {
        var sut = new PlatformTenantContext(NullLogger<PlatformTenantContext>.Instance);
        var changes = 0;
        sut.Changed += () => changes++;

        sut.SelectOrganization(Guid.NewGuid(), "Org A", "org-a");
        sut.SelectClinic(Guid.NewGuid(), "Clinic A");
        sut.HasClinic.Should().BeTrue();

        var nextOrg = Guid.NewGuid();
        sut.SelectOrganization(nextOrg, "Org B", "org-b");

        sut.SelectedOrganizationId.Should().Be(nextOrg);
        sut.SelectedOrganizationName.Should().Be("Org B");
        sut.HasClinic.Should().BeFalse();
        sut.SelectedClinicId.Should().BeNull();
        sut.ExplicitBypassEnabled.Should().BeTrue();
        changes.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Clearing_Organization_Clears_All_Scope()
    {
        var sut = new PlatformTenantContext(NullLogger<PlatformTenantContext>.Instance);
        sut.SelectOrganization(Guid.NewGuid(), "Org A");
        sut.SelectClinic(Guid.NewGuid(), "Clinic A");

        sut.Clear();

        sut.HasOrganization.Should().BeFalse();
        sut.HasClinic.Should().BeFalse();
        sut.ExplicitBypassEnabled.Should().BeFalse();
        sut.SelectedOrganizationId.Should().BeNull();
        sut.SelectedClinicId.Should().BeNull();
    }

    [Fact]
    public void Clearing_Clinic_Keeps_Organization()
    {
        var sut = new PlatformTenantContext(NullLogger<PlatformTenantContext>.Instance);
        var orgId = Guid.NewGuid();
        sut.SelectOrganization(orgId, "Org A");
        sut.SelectClinic(Guid.NewGuid(), "Clinic A");

        sut.ClearClinic();

        sut.SelectedOrganizationId.Should().Be(orgId);
        sut.HasClinic.Should().BeFalse();
    }

    [Fact]
    public void SelectClinic_Requires_Organization()
    {
        var sut = new PlatformTenantContext(NullLogger<PlatformTenantContext>.Instance);
        var act = () => sut.SelectClinic(Guid.NewGuid(), "Clinic");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Does_Not_Auto_Select_First_Organization()
    {
        var sut = new PlatformTenantContext(NullLogger<PlatformTenantContext>.Instance);
        sut.HasOrganization.Should().BeFalse();
        sut.ExplicitBypassEnabled.Should().BeFalse();
    }
}

public sealed class PlatformTenantArchitectureTests
{
    [Fact]
    public void Organization_Directory_Client_Is_Typed()
    {
        typeof(IOrganizationDirectoryApiClient)
            .GetMethod(nameof(IOrganizationDirectoryApiClient.SearchOrganizationsAsync))
            .Should().NotBeNull();
        typeof(IOrganizationDirectoryApiClient)
            .GetMethod(nameof(IOrganizationDirectoryApiClient.GetOrganizationAsync))
            .Should().NotBeNull();
    }

    [Fact]
    public void Platform_Tenant_State_Is_Centralized()
    {
        typeof(IPlatformTenantContext).Namespace.Should().Be("HealthCare.Web.Auth");
        typeof(PlatformTenantContext).Should().BeAssignableTo<IPlatformTenantContext>();
        typeof(WebPermissions).GetField(nameof(WebPermissions.OrganizationsRead)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.OrganizationsSelect)).Should().NotBeNull();
    }

    [Fact]
    public void Pages_Do_Not_Contain_Free_Text_Organization_Id_Fields()
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var pages = Directory.GetFiles(webRoot, "*.razor", SearchOption.AllDirectories);
        pages.Should().NotBeEmpty();

        foreach (var file in pages)
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain(
                "Organization ID (platform bypass)",
                because: $"{Path.GetFileName(file)} must not accept free-text OrganizationId");
            text.Should().NotContain(
                "_platformOrgIdText",
                because: $"{Path.GetFileName(file)} must use IPlatformTenantContext");
        }
    }

    [Fact]
    public void Web_Does_Not_Implement_Tenant_Authorization()
    {
        typeof(PlatformTenantContext).GetMethods()
            .Select(m => m.Name)
            .Should()
            .NotContain(n => n.Contains("Authorize", StringComparison.OrdinalIgnoreCase)
                             || n.Contains("CanAccess", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OrganizationPicker_Exists_For_Platform_Admin()
    {
        var picker = typeof(IOrganizationDirectoryApiClient).Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "OrganizationPicker");
        // Generated razor component type name may be OrganizationPicker in Components.Organizations
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        File.Exists(Path.Combine(webRoot, "Components", "Organizations", "OrganizationPicker.razor"))
            .Should().BeTrue();
        File.Exists(Path.Combine(webRoot, "Components", "Organizations", "PlatformTenantBanner.razor"))
            .Should().BeTrue();
    }

    [Fact]
    public void Medical_Note_Permissions_Remain_Absent_From_Web_Catalog_Duplicates()
    {
        // Web must not recreate RolePermissionMatrix; medical-note UI is intentionally absent.
        typeof(IOrganizationDirectoryApiClient).Assembly.GetTypes()
            .Select(t => t.Name)
            .Should()
            .NotContain("RolePermissionMatrix");

        typeof(WebPermissions).GetFields()
            .Select(f => f.Name)
            .Should()
            .NotContain(n => n.Contains("MedicalNote", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Permission_State_Does_Not_Store_Tenant_Selection()
    {
        typeof(IPermissionState).GetProperties().Select(p => p.Name)
            .Should().NotContain("SelectedOrganizationId");
        typeof(IPlatformTenantContext).GetProperties().Select(p => p.Name)
            .Should().Contain("SelectedOrganizationId");
    }
}
