using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace HealthCare.Web.Tests;

public sealed class AuthRedirectSupportTests
{
    [Theory]
    [InlineData("/appointments", "/appointments")]
    [InlineData("%2Fstaff", "/staff")]
    [InlineData("/dashboard?tab=1", "/dashboard?tab=1")]
    public void Valid_local_return_url_accepted(string input, string expected)
    {
        SafeReturnUrl.TryValidate(input, out var path).Should().BeTrue();
        path.Should().Be(expected);
        SafeReturnUrl.Resolve(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("http://evil.example")]
    [InlineData("//evil.example/path")]
    [InlineData("/\\evil.example")]
    [InlineData("appointments")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("/login")]
    [InlineData("/login?x=1")]
    public void Absolute_protocol_relative_or_login_return_url_rejected(string? input)
    {
        SafeReturnUrl.TryValidate(input, out _).Should().BeFalse();
        SafeReturnUrl.Resolve(input).Should().Be(SafeReturnUrl.DefaultPath);
    }

    [Fact]
    public void BuildLoginUrl_preserves_safe_return_url()
    {
        SafeReturnUrl.BuildLoginUrl("/appointments")
            .Should().Be("/login?returnUrl=%2Fappointments");
    }

    [Fact]
    public void BuildLoginUrl_defaults_without_unsafe_path()
    {
        SafeReturnUrl.BuildLoginUrl("https://evil.example").Should().Be("/login");
        SafeReturnUrl.BuildLoginUrl("/login").Should().Be("/login");
    }

    [Fact]
    public void Invalid_return_url_falls_back_to_dashboard()
    {
        SafeReturnUrl.Resolve("//evil").Should().Be("/dashboard");
        SafeReturnUrl.Resolve(null).Should().Be("/dashboard");
    }

    [Fact]
    public void Cookie_principal_excludes_tokens()
    {
        var user = new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "staff@test.local",
            Roles = ["RECEPTIONIST"],
            Permissions = [WebPermissions.AppointmentsRead],
            HasActiveStaffMembership = true,
        };

        var principal = StaffWebAuthCookie.CreatePrincipal(user);
        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.Claims.Should().NotContain(c =>
            c.Type.Contains("token", StringComparison.OrdinalIgnoreCase)
            || c.Type.Contains("access", StringComparison.OrdinalIgnoreCase)
            || c.Type.Contains("refresh", StringComparison.OrdinalIgnoreCase));
        principal.FindFirst("permission").Should().BeNull();
    }

    [Fact]
    public void Cookie_scheme_name_is_configured_default()
    {
        CookieAuthenticationDefaults.AuthenticationScheme.Should().Be("Cookies");
    }

    [Fact]
    public async Task Patient_is_authenticated_but_not_staff()
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
    }

    [Fact]
    public async Task Authenticated_unauthorized_staff_is_not_patient_only()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "rec@test.local",
            Roles = ["RECEPTIONIST"],
            Permissions = [],
            HasActiveStaffMembership = true,
            OrganizationId = Guid.NewGuid(),
            ClinicId = Guid.NewGuid(),
        });

        state.IsPatientOnly.Should().BeFalse();
        state.IsStaffUser.Should().BeTrue();
        state.Has(WebPermissions.AppointmentsRead).Should().BeFalse();
    }

    [Fact]
    public void FromNavigationUri_rejects_external_host()
    {
        var path = SafeReturnUrl.FromNavigationUri(
            "https://evil.example/appointments",
            "http://localhost:5018/");
        path.Should().Be(SafeReturnUrl.DefaultPath);
    }

    [Fact]
    public void FromNavigationUri_accepts_same_host_path()
    {
        var path = SafeReturnUrl.FromNavigationUri(
            "http://localhost:5018/appointments",
            "http://localhost:5018/");
        path.Should().Be("/appointments");
    }
}
