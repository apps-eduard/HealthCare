using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.Clinics;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminDashboardClinicUiTests
{
    [Fact]
    public void Clinic_Slug_Suggestion_Is_Lowercase_Hyphenated()
    {
        ClinicSlugHelper.SuggestFromName("Downtown Clinic!").Should().Be("downtown-clinic");
        ClinicSlugHelper.LooksValid("downtown-clinic").Should().BeTrue();
        ClinicSlugHelper.LooksValid("Downtown").Should().BeFalse();
        ClinicSlugHelper.LooksValid("bad_slug").Should().BeFalse();
    }

    [Fact]
    public void Clinic_Problem_Messages_Are_Safe_For_Known_Codes()
    {
        var conflict = new ApiProblemException(409, "Conflict", null, ClinicManagementErrorCodes.ConcurrencyConflict);
        ClinicProblemMessages.From(conflict).Should().Contain("Reload");

        var lastActive = new ApiProblemException(
            409,
            "Cannot deactivate the last active clinic",
            null,
            ClinicManagementErrorCodes.DeactivationNotAllowed);
        ClinicProblemMessages.From(lastActive).Should().Contain("last active");

        var limit = new ApiProblemException(409, "Limit", null, ClinicManagementErrorCodes.LimitReached);
        ClinicProblemMessages.From(limit).Should().Contain("limit");
    }

    [Fact]
    public void Clinic_Working_Context_Supports_All_Clinics_And_Selection()
    {
        var ctx = new ClinicWorkingContext();
        ctx.HasClinic.Should().BeFalse();
        ctx.SelectedClinicId.Should().BeNull();

        var id = Guid.NewGuid();
        ctx.SelectClinic(id, "North", isActive: true);
        ctx.HasClinic.Should().BeTrue();
        ctx.SelectedClinicId.Should().Be(id);
        ctx.SelectedClinicName.Should().Be("North");
        ctx.SelectedClinicIsActive.Should().BeTrue();

        var changed = 0;
        ctx.Changed += () => changed++;
        ctx.SelectClinic(id, "North", isActive: true);
        changed.Should().Be(0);

        ctx.ClearClinic();
        ctx.HasClinic.Should().BeFalse();
        changed.Should().Be(1);
    }

    [Fact]
    public async Task Clinics_Read_Is_Enough_For_Directory_Gate()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions =
            [
                WebPermissions.OrganizationDashboardRead,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.ClinicsRead).Should().BeTrue();
        state.Has(WebPermissions.ClinicsCreate).Should().BeFalse();
        state.Has(WebPermissions.OrganizationDashboardRead).Should().BeTrue();
        state.IsOrganizationAdmin.Should().BeTrue();
        state.IsPatientOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Patient_Only_Is_Denied_Staff_Surface()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "patient@test.local",
            Roles = [WebRoles.Patient],
            Permissions = [],
            HasActiveStaffMembership = false,
        });

        state.IsPatientOnly.Should().BeTrue();
        state.IsStaffUser.Should().BeFalse();
        state.Has(WebPermissions.ClinicsRead).Should().BeFalse();
        state.Has(WebPermissions.OrganizationDashboardRead).Should().BeFalse();
    }

    [Fact]
    public void Dashboard_And_Clinic_Pages_Use_Typed_Clients_Not_HttpClient()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var dashboard = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Dashboard.razor"));
        var clinics = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Clinics.razor"));
        var create = File.ReadAllText(Path.Combine(webRoot, "Components", "Clinics", "CreateClinicDialog.razor"));

        dashboard.Should().Contain("IOrganizationDashboardApiClient");
        dashboard.Should().NotContain("@inject HttpClient");
        dashboard.Should().Contain("/clinics");
        dashboard.Should().NotContain("Clinics via staff scope");

        clinics.Should().Contain("IClinicManagementApiClient");
        clinics.Should().Contain("ClinicsRead");
        clinics.Should().Contain("ClinicDetailDrawer");
        clinics.Should().NotContain("@inject HttpClient");
        clinics.Should().NotContain("type=\"text\" placeholder=\"OrganizationId\"");

        create.Should().Contain("ListTimeZonesAsync");
        create.Should().Contain("_includeAdmin");
        create.Should().Contain("_adminPassword = null");
        create.Should().NotContain("@inject HttpClient");
        create.Should().NotContain("label for=\"create-organization-id\"");
        create.Should().Contain("Initial Clinic Admin");
    }

    [Fact]
    public void Clinic_Management_Client_Sends_Server_Side_Query_Parameters()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "ClinicManagementApiClient.cs")));

        source.Should().Contain("search=");
        source.Should().Contain("isActive=");
        source.Should().Contain("sortBy=");
        source.Should().Contain("sortDirection=");
        source.Should().Contain("page=");
        source.Should().Contain("pageSize=");
        File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "HealthCare.Web", "Components", "Clinics", "EditClinicDialog.razor")))
            .Should().Contain("ExpectedVersion = _version");
        File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "HealthCare.Web", "Components", "Clinics", "ClinicActivationDialog.razor")))
            .Should().Contain("ExpectedVersion = Options.ExpectedVersion");
    }

    [Fact]
    public void Web_Permissions_Include_Clinic_Lifecycle_Constants()
    {
        typeof(WebPermissions).GetField(nameof(WebPermissions.ClinicsRead)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.ClinicsCreate)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.ClinicsUpdate)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.ClinicsActivate)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.ClinicsDeactivate)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.OrganizationDashboardRead)).Should().NotBeNull();
    }
}
