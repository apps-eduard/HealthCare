using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Organizations;
using HealthCare.Web.Auth;
using HealthCare.Web.Reports;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class OrganizationAdminReportsUiTests
{
    [Fact]
    public async Task Organization_Admin_Can_View_Reports()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.OrganizationReportsRead, WebPermissions.ClinicsRead],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.Has(WebPermissions.OrganizationReportsRead).Should().BeTrue();
        state.CanFilterByClinic.Should().BeTrue();
    }

    [Fact]
    public void Catalog_Covers_Backend_Report_Types()
    {
        OrganizationReportCatalog.All.Select(r => r.Type)
            .Should().BeEquivalentTo(OrganizationReportTypes.All);
        OrganizationReportCatalog.All.Should().OnlyContain(r => r.SupportsCsv);
        OrganizationReportCatalog.MaxInclusiveDays.Should().Be(93);
    }

    [Theory]
    [InlineData(0, 0, "—")]
    [InlineData(1, 0, "—")]
    [InlineData(1, 4, "25%")]
    [InlineData(1, 3, "33.3%")]
    public void Display_Rates_Handle_Zero_Denominators(int numerator, int denominator, string expected) =>
        OrganizationReportPresentation.FormatPercent(numerator, denominator).Should().Be(expected);

    [Fact]
    public void Timezone_Strategy_Labels_Are_Centralized()
    {
        OrganizationReportPresentation.TimeZoneStrategyLabel("clinic")
            .Should().Contain("clinic local");
        OrganizationReportPresentation.TimeZoneStrategyLabel("per_clinic_local")
            .Should().Contain("Per-clinic");
    }

    [Fact]
    public void Problem_Messages_Map_Date_Range_And_Scope()
    {
        var range = new ApiProblemException(400, "Bad", "raw", OrganizationReportErrorCodes.InvalidDateRange);
        OrganizationReportProblemMessages.ToUserMessage(range).Should().Contain("93");
        OrganizationReportProblemMessages.ToUserMessage(range).Should().NotContain("raw");

        var scope = new ApiProblemException(400, "Bad", null, OrganizationReportErrorCodes.OrganizationScopeRequired);
        OrganizationReportProblemMessages.ToUserMessage(scope).Should().Contain("organization");
    }

    [Fact]
    public void Reports_Page_Uses_Clinic_Context_Catalog_And_Csv_Download()
    {
        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var page = File.ReadAllText(Path.Combine(webRoot, "Components", "Pages", "Reports.razor"));
        var layout = File.ReadAllText(Path.Combine(webRoot, "Components", "Layout", "StaffLayout.razor"));
        var shell = File.ReadAllText(Path.Combine(webRoot, "wwwroot", "js", "healthcare-shell.js"));

        page.Should().Contain("IClinicWorkingContext");
        page.Should().Contain("IOrganizationReportApiClient");
        page.Should().Contain("IBrowserFileDownload");
        page.Should().Contain("OrganizationReportCatalog");
        page.Should().Contain("ExportCsvAsync");
        page.Should().Contain("OrganizationReportsRead");
        page.Should().NotContain("@inject HttpClient");

        layout.Should().Contain("/reports");
        layout.Should().Contain("Reports");
        layout.Should().Contain("OrganizationReportsRead");

        shell.Should().Contain("downloadBase64File");
    }

    [Fact]
    public void Report_Client_Exposes_All_Endpoints_And_Csv()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "HealthCare.Web", "Services", "OrganizationReportApiClient.cs")));

        source.Should().Contain("api/v1/organization/reports/");
        source.Should().Contain("\"appointments\"");
        source.Should().Contain("\"staff\"");
        source.Should().Contain("\"patients\"");
        source.Should().Contain("\"availability\"");
        source.Should().Contain("reminder-failures");
        source.Should().Contain("summary-failures");
        source.Should().Contain("export.csv");
        source.Should().Contain("DownloadedFile");
        source.Should().Contain("IBrowserFileDownload");
    }
}
