using System.Text.Json;
using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Doctors;
using HealthCare.Contracts.Doctors;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class DoctorDashboardServiceTests
{
    [Fact]
    public void Doctor_Dashboard_Permission_Is_Granted_To_Doctor_And_Platform_Admin_Only()
    {
        Permissions.All.Should().Contain(Permissions.Doctors.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor)
            .Should().Contain(Permissions.Doctors.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Doctors.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().NotContain(Permissions.Doctors.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().NotContain(Permissions.Doctors.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Nurse)
            .Should().NotContain(Permissions.Doctors.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Receptionist)
            .Should().NotContain(Permissions.Doctors.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Patient)
            .Should().NotContain(Permissions.Doctors.DashboardRead);
    }

    [Fact]
    public async Task Doctor_Sees_Only_Own_Appointment_Aggregates()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctorA = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-a-dash@test.local");
        var doctorB = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-b-dash@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-c-dash@test.local");

        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var today = converter.GetClinicDate(h.Clock.GetUtcNow(), h.ClinicA.TimeZoneId);

        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.Confirmed, today, doctorA.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.CheckedIn, today, doctorA.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.Confirmed, today.AddDays(1), doctorA.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.Confirmed, today, doctorB.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(
            h.ClinicA.Id,
            AppointmentStatus.NoShow,
            today.AddDays(-2),
            doctorA.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(
            h.ClinicB.Id,
            AppointmentStatus.Confirmed,
            today,
            doctorStaffMemberId: null);

        var sut = h.CreateDoctorDashboardService(doctorA);
        var result = await sut.GetAsync(new DoctorDashboardQuery());

        result.DoctorStaffMemberId.Should().Be(doctorA.Staff.Id);
        result.ClinicId.Should().Be(h.ClinicA.Id);
        result.OrganizationId.Should().Be(h.Org.Id);
        result.OrganizationName.Should().Be(h.Org.Name);
        result.DefaultTimeZoneId.Should().Be("Asia/Riyadh");
        result.TimeZoneStrategy.Should().Be("clinic");
        result.TodayAppointmentCount.Should().Be(2);
        result.CheckedInAppointmentCount.Should().Be(1);
        result.AwaitingCompletionCount.Should().Be(1);
        result.RecentNoShowCount.Should().Be(1);
        result.UpcomingAppointmentCount.Should().BeGreaterThanOrEqualTo(1);
        result.NextAppointment.Should().NotBeNull();
        result.NextAppointment!.AppointmentId.Should().NotBe(Guid.Empty);

        var json = JsonSerializer.Serialize(result);
        json.Should().NotContain("ActiveStaffCount");
        json.Should().NotContain("ActivePatientCount");
        json.Should().NotContain("Subjective");
        json.Should().NotContain("Assessment");
        json.ToLowerInvariant().Should().NotContain("billing");
        json.ToLowerInvariant().Should().NotContain("maxclinics");
    }

    [Fact]
    public async Task Doctor_Cannot_Select_Another_Doctor_Or_Clinic()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctorA = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-scope-a@test.local");
        var doctorB = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-scope-b@test.local");
        var sut = h.CreateDoctorDashboardService(doctorA);

        await FluentActions.Awaiting(() => sut.GetAsync(new DoctorDashboardQuery { ClinicId = h.ClinicB.Id }))
            .Should().ThrowAsync<DoctorDashboardException>()
            .Where(e => e.ErrorCode == DoctorDashboardErrorCodes.InvalidScope);

        await FluentActions.Awaiting(() => sut.GetAsync(new DoctorDashboardQuery
            {
                DoctorStaffMemberId = doctorB.Staff.Id,
            }))
            .Should().ThrowAsync<DoctorDashboardException>()
            .Where(e => e.ErrorCode == DoctorDashboardErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Non_Doctor_And_Clinic_Admin_Are_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-doc-dash@test.local");
        var nurse = await h.SeedStaffAsync(AppRoles.Nurse, h.ClinicA.Id, "nurse-doc-dash@test.local");

        await FluentActions.Awaiting(() => h.CreateDoctorDashboardService(clinicAdmin).GetAsync(new DoctorDashboardQuery()))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => h.CreateDoctorDashboardService(nurse).GetAsync(new DoctorDashboardQuery()))
            .Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Inactive_Doctor_Membership_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-inactive-dash@test.local");
        doctor.Staff.IsActive = false;
        await h.Db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = doctor.User.Id,
            Email = doctor.User.Email,
            Roles = [AppRoles.Doctor],
            OrganizationId = doctor.Staff.OrganizationId,
            ClinicId = doctor.Staff.ClinicId,
            StaffMemberId = doctor.Staff.Id,
        };
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        var sut = h.BuildDoctorDashboardService(currentUser, currentStaff);

        await FluentActions.Awaiting(() => sut.GetAsync(new DoctorDashboardQuery()))
            .Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_Clinic_And_Doctor()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-pa-dash@test.local");
        var platform = await h.SeedPlatformAdminAsync("plat-doc-dash@test.local");
        var sut = h.CreatePlatformDoctorDashboardService(platform);

        await FluentActions.Awaiting(() => sut.GetAsync(new DoctorDashboardQuery
            {
                ClinicId = h.ClinicA.Id,
                DoctorStaffMemberId = doctor.Staff.Id,
            }))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorDashboardQuery { DoctorStaffMemberId = doctor.Staff.Id },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorDashboardException>()
            .Where(e => e.ErrorCode == DoctorDashboardErrorCodes.ClinicScopeRequired);

        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorDashboardQuery { ClinicId = h.ClinicA.Id },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorDashboardException>()
            .Where(e => e.ErrorCode == DoctorDashboardErrorCodes.DoctorScopeRequired);

        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorDashboardQuery
                {
                    ClinicId = h.ClinicA.Id,
                    DoctorStaffMemberId = Guid.NewGuid(),
                },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorDashboardException>()
            .Where(e => e.ErrorCode == DoctorDashboardErrorCodes.DoctorNotFound);

        var doctorInB = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-pa-b@test.local");
        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorDashboardQuery
                {
                    ClinicId = h.ClinicA.Id,
                    DoctorStaffMemberId = doctorInB.Staff.Id,
                },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorDashboardException>()
            .Where(e => e.ErrorCode == DoctorDashboardErrorCodes.DoctorNotFound);

        var ok = await sut.GetAsync(
            new DoctorDashboardQuery
            {
                ClinicId = h.ClinicA.Id,
                DoctorStaffMemberId = doctor.Staff.Id,
            },
            PlatformAdminBypass.Explicit);
        ok.DoctorStaffMemberId.Should().Be(doctor.Staff.Id);
        ok.ClinicId.Should().Be(h.ClinicA.Id);
    }

    [Fact]
    public async Task Date_Boundaries_Use_Clinic_Timezone()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-tz-dash@test.local");
        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var today = converter.GetClinicDate(h.Clock.GetUtcNow(), h.ClinicA.TimeZoneId);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.Confirmed, today, doctor.Staff.Id);

        var sut = h.CreateDoctorDashboardService(doctor);
        var result = await sut.GetAsync(new DoctorDashboardQuery());
        result.LocalDashboardDate.Should().Be(today.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Availability_Warning_Is_Self_Scoped_When_Missing_Weekly_Window()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-avail-warn@test.local");
        var sut = h.CreateDoctorDashboardService(doctor);
        var result = await sut.GetAsync(new DoctorDashboardQuery());
        result.AvailabilityWarningCount.Should().BeGreaterThan(0);
        result.AvailabilityWarnings.Should().Contain(w => w.Contains("weekly availability", StringComparison.OrdinalIgnoreCase));
    }
}
