using FluentAssertions;
using HealthCare.Contracts.Doctors;
using HealthCare.Contracts.Identity;
using HealthCare.Web.Auth;
using HealthCare.Web.DoctorDashboard;
using HealthCare.Web.DoctorProfile;
using HealthCare.Web.Services;

namespace HealthCare.Web.Tests;

public sealed class DoctorProfileUiTests
{
    [Fact]
    public async Task Doctor_Sees_My_Profile_Navigation_And_Permissions()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doctor@test.local",
            Roles = [WebRoles.Doctor],
            Permissions =
            [
                WebPermissions.DoctorDashboardRead,
                WebPermissions.DoctorProfileRead,
                WebPermissions.DoctorProfileUpdate,
                WebPermissions.AppointmentsRead,
                WebPermissions.AvailabilityRead,
                WebPermissions.AvailabilityManageSelf,
                WebPermissions.PatientsSearch,
                WebPermissions.ClinicsRead,
            ],
            HasActiveStaffMembership = true,
            ClinicId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            StaffMemberId = Guid.NewGuid(),
        });

        DoctorProfilePermissionRules.CanView(state).Should().BeTrue();
        DoctorProfilePermissionRules.CanUpdate(state).Should().BeTrue();
        DoctorConsoleNavigation.IsDoctorConsoleActor(state).Should().BeTrue();
        DoctorConsoleNavigation.ShowMyProfileLink(state).Should().BeTrue();
        DoctorConsoleNavigation.ShowPatientsLink(state).Should().BeFalse();
    }

    [Fact]
    public async Task Read_Only_User_Cannot_Edit()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "doctor-ro@test.local",
            Roles = [WebRoles.Doctor],
            Permissions =
            [
                WebPermissions.DoctorDashboardRead,
                WebPermissions.DoctorProfileRead,
            ],
            HasActiveStaffMembership = true,
            ClinicId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            StaffMemberId = Guid.NewGuid(),
        });

        DoctorProfilePermissionRules.CanView(state).Should().BeTrue();
        DoctorProfilePermissionRules.CanUpdate(state).Should().BeFalse();
        DoctorConsoleNavigation.ShowMyProfileLink(state).Should().BeTrue();
    }

    [Fact]
    public async Task Clinic_And_Org_Admin_Do_Not_See_Doctor_Profile_Permission()
    {
        var clinic = new PermissionState();
        await clinic.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "ca@test.local",
            Roles = [WebRoles.ClinicAdmin],
            Permissions = [WebPermissions.ClinicDashboardRead, WebPermissions.ClinicProfileRead],
            HasActiveStaffMembership = true,
            ClinicId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
        });
        DoctorProfilePermissionRules.CanView(clinic).Should().BeFalse();
        DoctorConsoleNavigation.ShowMyProfileLink(clinic).Should().BeFalse();

        var org = new PermissionState();
        await org.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "oa@test.local",
            Roles = [WebRoles.OrganizationAdmin],
            Permissions = [WebPermissions.OrganizationDashboardRead, WebPermissions.OrganizationProfileRead],
            HasActiveStaffMembership = true,
            ClinicId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
        });
        DoctorProfilePermissionRules.CanView(org).Should().BeFalse();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Doctor_Context()
    {
        var state = new PermissionState();
        await state.SetFromUserAsync(new CurrentUserResponse
        {
            UserId = Guid.NewGuid(),
            Email = "admin@test.local",
            Roles = [WebRoles.PlatformAdmin],
            Permissions =
            [
                WebPermissions.DoctorProfileRead,
                WebPermissions.DoctorProfileUpdate,
            ],
            HasActiveStaffMembership = false,
        });

        DoctorProfilePermissionRules.CanView(state).Should().BeTrue();
        state.IsPlatformAdmin.Should().BeTrue();
        DoctorConsoleNavigation.ShowMyProfileLink(state).Should().BeFalse();
    }

    [Fact]
    public void Form_State_Builds_Partial_Update_And_Validates()
    {
        var form = new DoctorProfileFormState
        {
            DisplayName = "Dr. Ada",
            FirstName = "Ada",
            LastName = "Lovelace",
            JobTitle = "Consultant",
            ContactPhone = "+966500000000",
            ExpectedVersion = 3,
        };

        var request = form.TryBuildUpdateRequest(out var error);
        error.Should().BeNull();
        request.Should().NotBeNull();
        request!.ExpectedVersion.Should().Be(3);
        request.DisplayName.Should().Be("Dr. Ada");
        request.FirstName.Should().Be("Ada");
        request.ContactPhone.Should().Be("+966500000000");

        form.FirstName = "";
        form.TryBuildUpdateRequest(out var missingFirst).Should().BeNull();
        missingFirst.Should().Contain("First name");

        form.FirstName = "Ada";
        form.ContactPhone = new string('9', 31);
        form.TryBuildUpdateRequest(out var phoneError).Should().BeNull();
        phoneError.Should().Contain("30");
    }

    [Fact]
    public void Profile_Page_Has_Read_Only_And_Editable_Surfaces()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Components", "Pages", "DoctorProfile.razor");
        var source = File.ReadAllText(path);

        source.Should().Contain("@page \"/doctor/profile\"");
        source.Should().Contain("My Profile");
        source.Should().Contain("doctor-profile-clinic");
        source.Should().Contain("doctor-profile-organization");
        source.Should().Contain("doctor-profile-email");
        source.Should().Contain("doctor-profile-role");
        source.Should().Contain("doctor-profile-specialty");
        source.Should().Contain("Status is read-only");
        source.Should().Contain("doctor-profile-display-name");
        source.Should().Contain("doctor-profile-first-name");
        source.Should().Contain("doctor-profile-last-name");
        source.Should().Contain("doctor-profile-job-title");
        source.Should().Contain("doctor-profile-phone");
        source.Should().Contain("Reload latest");
        source.Should().Contain("Explicit doctor context required");
        source.Should().NotContain("id=\"doctor-profile-email-edit\"");
        source.Should().NotContain("id=\"doctor-profile-role-edit\"");
        source.Should().NotContain("id=\"doctor-profile-specialty-edit\"");
        source.Should().NotContain("MaxClinics");
        source.ToLowerInvariant().Should().NotContain("billing");
        source.ToLowerInvariant().Should().NotContain("medical note");
    }

    [Fact]
    public void StaffLayout_Shows_My_Profile_For_Doctor_Console()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "HealthCare.Web", "Components", "Layout", "StaffLayout.razor");
        var source = File.ReadAllText(path);
        source.Should().Contain("DoctorConsoleNavigation.ShowMyProfileLink");
        source.Should().Contain("/doctor/profile");
        source.Should().Contain("My Profile");
        source.Should().Contain("doctor-profile");
        source.Should().Contain("DoctorConsoleNavigation.ShowPatientsLink");
    }

    [Fact]
    public void Doctor_Profile_Api_Client_And_Problem_Messages_Exist()
    {
        typeof(IDoctorProfileApiClient).Should().NotBeNull();
        typeof(DoctorProfileApiClient).Should().NotBeNull();
        typeof(DoctorProfileResponse).Should().NotBeNull();

        var conflict = new ApiProblemException(409, "Conflict", null, DoctorProfileErrorCodes.ConcurrencyConflict);
        DoctorProfileProblemMessages.ToUserMessage(conflict).Should().Contain("Reload");

        var forbidden = new ApiProblemException(403, "Forbidden", null, DoctorProfileErrorCodes.AccessDenied);
        DoctorProfileProblemMessages.ToUserMessage(forbidden).Should().Contain("permission");

        var unauthorized = new ApiProblemException(401, "Unauthorized", null, null);
        DoctorProfileProblemMessages.ToUserMessage(unauthorized).Should().Contain("Sign in");

        var notFound = new ApiProblemException(404, "Not found", null, DoctorProfileErrorCodes.DoctorNotFound);
        DoctorProfileProblemMessages.ToUserMessage(notFound).Should().Contain("not found");

        var badRequest = new ApiProblemException(400, "Bad", null, DoctorProfileErrorCodes.EmptyUpdate);
        DoctorProfileProblemMessages.ToUserMessage(badRequest).Should().Contain("at least one");
    }

    [Fact]
    public void Presentation_Resolves_Display_Name_And_Status()
    {
        var profile = new DoctorProfileResponse
        {
            StaffMemberId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            OrganizationName = "Org",
            ClinicId = Guid.NewGuid(),
            ClinicName = "Clinic",
            Email = "a@b.c",
            Role = "DOCTOR",
            FirstName = "Ada",
            LastName = "Lovelace",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            Version = 1,
        };

        DoctorProfilePresentation.ResolveDisplayName(profile).Should().Be("Ada Lovelace");
        DoctorProfilePresentation.StatusText(true).Should().Be("Active");
        DoctorProfilePresentation.DisplayOrDash(null).Should().Be("—");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "HealthCare.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}
