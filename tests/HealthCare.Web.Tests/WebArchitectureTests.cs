using System.Reflection;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Appointments;
using HealthCare.Web.Auth;
using HealthCare.Web.Availability;
using HealthCare.Web.Patients;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

/// <summary>
/// Architecture-style checks for HealthCare.Web (kept here to avoid ambiguous Program
/// when ArchitectureTests references both Api and Web).
/// </summary>
public sealed class WebArchitectureTests
{
    [Fact]
    public void Web_Does_Not_Reference_Infrastructure()
    {
        var refs = typeof(AppointmentApiClient).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        refs.Should().NotContain("HealthCare.Infrastructure");
        refs.Should().NotContain("HealthCare.Application");
        refs.Should().Contain("HealthCare.Contracts");
    }

    [Fact]
    public void Web_Uses_AntDesign_Not_FluentUI_Or_MudBlazor()
    {
        var csproj = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web", "HealthCare.Web.csproj"));
        var text = File.ReadAllText(csproj);
        text.Should().Contain("AntDesign");
        text.Should().Contain("1.6.2");
        text.Should().NotContain("Microsoft.FluentUI.AspNetCore.Components");
        text.Should().NotContain("MudBlazor");

        var refs = typeof(AppointmentApiClient).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();
        refs.Should().Contain("AntDesign");
        refs.Should().NotContain("Microsoft.FluentUI.AspNetCore.Components");
        refs.Should().NotContain("MudBlazor");

        var program = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web", "Program.cs")));
        program.Should().Contain("AddAntDesign()");
        program.Should().NotContain("AddFluentUIComponents");

        var appRazor = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web", "Components", "App.razor")));
        appRazor.Should().Contain("<AntContainer");
        appRazor.Should().Contain("_content/AntDesign/");
        appRazor.Should().NotContain("Microsoft.FluentUI");

        var webRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        foreach (var file in Directory.GetFiles(webRoot, "*.razor", SearchOption.AllDirectories))
        {
            var razor = File.ReadAllText(file);
            razor.Should().NotContain("<Fluent", because: $"{Path.GetFileName(file)} must not use Fluent UI components");
            razor.Should().NotContain("@using Microsoft.FluentUI", because: $"{Path.GetFileName(file)} must not import Fluent UI");
        }
    }

    [Fact]
    public void Web_Does_Not_Duplicate_RolePermissionMatrix()
    {
        typeof(AppointmentApiClient).Assembly.GetTypes()
            .Select(t => t.Name)
            .Should()
            .NotContain("RolePermissionMatrix");
    }

    [Fact]
    public void Status_Presentation_And_Action_Rules_Are_Centralized()
    {
        typeof(AppointmentStatusPresentation).Namespace.Should().Be("HealthCare.Web.Appointments");
        typeof(AppointmentActionRules).Namespace.Should().Be("HealthCare.Web.Appointments");
        typeof(ClinicTimeDisplay).Namespace.Should().Be("HealthCare.Web.Appointments");
        typeof(AppointmentProblemMessages).Namespace.Should().Be("HealthCare.Web.Appointments");
    }

    [Fact]
    public void Availability_Presentation_Is_Centralized()
    {
        typeof(AvailabilityPresentation).Namespace.Should().Be("HealthCare.Web.Availability");
        typeof(AvailabilityPermissionRules).Namespace.Should().Be("HealthCare.Web.Availability");
        typeof(AvailabilityProblemMessages).Namespace.Should().Be("HealthCare.Web.Availability");
    }

    [Fact]
    public void Appointment_Actions_Use_Typed_Clients()
    {
        typeof(IAppointmentApiClient).GetMethod(nameof(IAppointmentApiClient.ConfirmAsync)).Should().NotBeNull();
        typeof(IAppointmentApiClient).GetMethod(nameof(IAppointmentApiClient.CheckInAsync)).Should().NotBeNull();
        typeof(IAppointmentApiClient).GetMethod(nameof(IAppointmentApiClient.CompleteAsync)).Should().NotBeNull();
        typeof(IAppointmentApiClient).GetMethod(nameof(IAppointmentApiClient.MarkNoShowAsync)).Should().NotBeNull();
        typeof(IAppointmentApiClient).GetMethod(nameof(IAppointmentApiClient.CancelAsync)).Should().NotBeNull();
        typeof(IAppointmentApiClient).GetMethod(nameof(IAppointmentApiClient.RescheduleAsync)).Should().NotBeNull();
        typeof(IAppointmentApiClient).GetMethod(nameof(IAppointmentApiClient.GetAvailableSlotsAsync)).Should().NotBeNull();
        typeof(IStaffPatientApiClient).GetMethod(nameof(IStaffPatientApiClient.SearchAsync)).Should().NotBeNull();
        typeof(IStaffPatientApiClient).GetMethod(nameof(IStaffPatientApiClient.GetByIdAsync)).Should().NotBeNull();
        typeof(IStaffPatientApiClient).GetMethod(nameof(IStaffPatientApiClient.UpdateClinicProfileAsync)).Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.ListAvailabilityAsync))
            .Should().NotBeNull();
        typeof(IDoctorAvailabilityApiClient).GetMethod(nameof(IDoctorAvailabilityApiClient.GetAvailableSlotsAsync))
            .Should().NotBeNull();
        typeof(IOrganizationDirectoryApiClient).GetMethod(nameof(IOrganizationDirectoryApiClient.SearchOrganizationsAsync))
            .Should().NotBeNull();
    }

    [Fact]
    public void Patient_Status_Presentation_Is_Centralized()
    {
        typeof(PatientStatusPresentation).Namespace.Should().Be("HealthCare.Web.Patients");
        typeof(PatientDisplay).Namespace.Should().Be("HealthCare.Web.Patients");
        typeof(StaffPatientSearchQueryBuilder).Namespace.Should().Be("HealthCare.Web.Patients");
    }

    [Fact]
    public void Permission_Checks_Use_IPermissionState()
    {
        typeof(IPermissionState).IsInterface.Should().BeTrue();
        typeof(PermissionState).Should().BeAssignableTo<IPermissionState>();
        typeof(WebPermissions).GetField(nameof(WebPermissions.AppointmentsRead)).Should().NotBeNull();
        typeof(WebPermissions).GetField(nameof(WebPermissions.AvailabilityManageSelf)).Should().NotBeNull();
    }

    [Fact]
    public void Pages_Do_Not_Declare_Raw_HttpClient_Fields()
    {
        var pageTypes = typeof(AppointmentApiClient).Assembly.GetTypes()
            .Where(t => t.Namespace == "HealthCare.Web.Components.Pages")
            .ToArray();

        foreach (var page in pageTypes)
        {
            var fields = page.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            fields.Select(f => f.FieldType)
                .Should()
                .NotContain(typeof(HttpClient), because: $"{page.Name} must use typed API clients");
        }
    }

    [Fact]
    public void Return_Url_And_Redirect_Helpers_Are_Centralized()
    {
        typeof(SafeReturnUrl).Namespace.Should().Be("HealthCare.Web.Auth");
        typeof(IStaffWebAuthCookie).Namespace.Should().Be("HealthCare.Web.Auth");
        typeof(StaffWebAuthCookie).GetMethod(nameof(StaffWebAuthCookie.CreatePrincipal))
            .Should().NotBeNull();
    }

    [Fact]
    public void Bff_Auth_Mutations_Are_Centralized_And_Post_Only()
    {
        typeof(HealthCare.Web.Endpoints.BffAuthEndpoints).Namespace.Should().Be("HealthCare.Web.Endpoints");
        typeof(IBffAuthService).Namespace.Should().Be("HealthCare.Web.Auth");
        typeof(IApiTokenSessionStore).GetMethod(nameof(IApiTokenSessionStore.TryUpdateTokensAsync))
            .Should().NotBeNull();

        var endpointsSource = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web", "Endpoints", "BffAuthEndpoints.cs"));
        var text = File.ReadAllText(endpointsSource);
        text.Should().Contain("MapPost(\"/login\"");
        text.Should().Contain("MapPost(\"/logout\"");
        text.Should().Contain("\"/establish\"");
        text.Should().Contain("EstablishRejectedAsync");
        text.Should().Contain("LogoutGetRejectedAsync");
        text.Should().Contain("ValidateRequestAsync");
        text.Should().Contain("Status405MethodNotAllowed");
        text.Should().NotContain("CreateLoginTicket");
        text.Should().NotContain("ConsumeLoginTicket");
        File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web", "Program.cs")))
            .Should().Contain("/bff/auth/establish");
    }

    [Fact]
    public void Session_Rotation_And_Logout_Are_Centralized()
    {
        typeof(IBffAuthService).GetMethod(nameof(IBffAuthService.LoginAsync)).Should().NotBeNull();
        typeof(IBffAuthService).GetMethod(nameof(IBffAuthService.LogoutAsync)).Should().NotBeNull();
        typeof(IApiTokenSessionStore).GetMethods()
            .Select(m => m.Name)
            .Should()
            .NotContain(n => n.Contains("Ticket", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Razor_Pages_Do_Not_Reference_AuthTokenResponse()
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "HealthCare.Web"));
        var razorFiles = Directory.GetFiles(webRoot, "*.razor", SearchOption.AllDirectories);
        foreach (var file in razorFiles)
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain(
                "AuthTokenResponse",
                because: $"{Path.GetFileName(file)} must not access API token DTOs");
            text.Should().NotContain(
                "AccessToken",
                because: $"{Path.GetFileName(file)} must not handle access tokens");
        }
    }

    [Fact]
    public void Web_Auth_Cookie_Does_Not_Store_Api_Tokens()
    {
        var source = typeof(StaffWebAuthCookie).Assembly.Location;
        source.Should().NotBeNullOrWhiteSpace();

        // Cookie principal factory must not accept or emit token claim types.
        var user = new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "a@b.c",
            Roles = ["DOCTOR"],
            Permissions = ["appointments.read"],
            HasActiveStaffMembership = true,
        };
        var principal = StaffWebAuthCookie.CreatePrincipal(user);
        string.Join(',', principal.Claims.Select(c => c.Type + "=" + c.Value))
            .Should().NotContain("eyJ"); // JWT prefix
        principal.Claims.Select(c => c.Type).Should().NotContain("access_token");
        principal.Claims.Select(c => c.Type).Should().NotContain("refresh_token");
    }

    [Fact]
    public void Web_Does_Not_Calculate_Availability_Slots()
    {
        typeof(AppointmentListQueryBuilder).GetMethods()
            .Select(m => m.Name)
            .Should()
            .NotContain(n => n.Contains("Generate", StringComparison.OrdinalIgnoreCase)
                             || n.Contains("Availability", StringComparison.OrdinalIgnoreCase));

        typeof(AvailabilityPresentation).GetMethods()
            .Select(m => m.Name)
            .Should()
            .NotContain(n => n.Contains("Generate", StringComparison.OrdinalIgnoreCase));
    }
}
