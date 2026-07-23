using System.Reflection;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Appointments;
using HealthCare.Web.Auth;
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
    }
}
