using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.Patients;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

public sealed class ClinicAdminDoctorDirectoryTests
{
    [Fact]
    public async Task Clinic_Admin_Lists_Own_Clinic_Doctors_By_Clinic_Id()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);

        var doctors = await h.CreateDirectory(
                admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin)
            .ListDoctorsByClinicIdAsync(data.ClinicAId);

        doctors.Should().ContainSingle(d => d.StaffMemberId == data.DoctorAStaffId);
        doctors.Should().NotContain(d => d.StaffMemberId == data.DoctorBStaffId);
    }

    [Fact]
    public async Task Non_Doctor_Staff_Excluded_From_Doctor_Directory()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.Nurse,
            FirstName = "Nina",
            LastName = "Nurse",
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        var doctors = await h.CreateDirectory(
                admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin)
            .ListDoctorsByClinicIdAsync(data.ClinicAId);

        doctors.Should().OnlyContain(d => d.StaffMemberId == data.DoctorAStaffId);
    }

    [Fact]
    public async Task Cross_Clinic_Doctor_List_Denied_For_Clinic_Admin()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);

        var act = () => h.CreateDirectory(
                admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin)
            .ListDoctorsByClinicIdAsync(data.ClinicBId);

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Inactive_Doctors_Excluded_From_Booking_Directory()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var doctor = await h.Db.StaffMembers.SingleAsync(s => s.Id == data.DoctorAStaffId);
        doctor.IsActive = false;
        await h.Db.SaveChangesAsync();

        var doctors = await h.CreateDirectory(
                admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin)
            .ListDoctorsByClinicIdAsync(data.ClinicAId);

        doctors.Should().BeEmpty();
    }

    [Fact]
    public async Task Doctor_Display_Name_Prefers_Person_Name_Over_Job_Title()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var doctor = await h.Db.StaffMembers.SingleAsync(s => s.Id == data.DoctorAStaffId);
        doctor.FirstName = "Ava";
        doctor.LastName = "Adams";
        doctor.JobTitle = "Cardiologist";
        doctor.DisplayName = null;
        await h.Db.SaveChangesAsync();

        var doctors = await h.CreateDirectory().ListDoctorsByClinicCodeAsync(data.ClinicASlug);
        doctors.Single().DisplayName.Should().Be("Ava Adams");
    }

    [Fact]
    public void Format_Doctor_Display_Name_Falls_Back_Through_Display_First_Last_JobTitle()
    {
        DoctorDirectoryService.FormatDoctorDisplayName("Preferred", "A", "B", "Title").Should().Be("Preferred");
        DoctorDirectoryService.FormatDoctorDisplayName(null, "Ava", "Adams", "Title").Should().Be("Ava Adams");
        DoctorDirectoryService.FormatDoctorDisplayName(" ", " ", " ", "Cardiologist").Should().Be("Cardiologist");
        DoctorDirectoryService.FormatDoctorDisplayName(null, null, null, null).Should().Be("Doctor");
    }

    [Fact]
    public async Task Clinic_Admin_Can_Manage_Own_Clinic_Doctor_Availability()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        var windows = await sut.ListAvailabilityAsync(data.DoctorAStaffId);
        windows.Should().NotBeEmpty();

        ClearDay(h, data.DoctorAStaffId, DayOfWeek.Friday);
        await h.Db.SaveChangesAsync();

        var created = await sut.CreateAvailabilityAsync(data.DoctorAStaffId, new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = nameof(DayOfWeek.Friday),
            StartLocalTime = "09:00",
            EndLocalTime = "11:00",
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

        created.StartLocalTime.Should().Be("09:00");
        created.ClinicId.Should().Be(data.ClinicAId);
    }

    [Fact]
    public async Task Cross_Clinic_Availability_Mutation_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        var act = () => sut.CreateAvailabilityAsync(data.DoctorBStaffId, new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = nameof(DayOfWeek.Monday),
            StartLocalTime = "09:00",
            EndLocalTime = "10:00",
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.DoctorNotFound);
    }

    [Fact]
    public async Task Inactive_Membership_Denied_For_Availability_Manage()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = admin.UserId,
            Roles = [AppRoles.ClinicAdmin],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = false,
            StaffMemberId = admin.StaffId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.ClinicAdmin,
        };
        var sut = new DoctorAvailabilityService(
            h.Db, user, staff, new NoOpAuthorizationAuditLogger(), Microsoft.Extensions.Logging.Abstractions.NullLogger<DoctorAvailabilityService>.Instance);

        var act = () => sut.ListAvailabilityAsync(data.DoctorAStaffId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Without_Explicit_Bypass_Denied_For_Clinic_Doctors()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var userId = Guid.NewGuid();
        var directory = new DoctorDirectoryService(
            h.Db,
            new ClinicPublicLookup(h.Db),
            new FakeCurrentUser
            {
                IsAuthenticated = true,
                UserId = userId,
                Roles = [AppRoles.PlatformAdmin],
            },
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger());

        var act = () => directory.ListDoctorsByClinicIdAsync(data.ClinicAId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_With_Explicit_Bypass_Lists_Clinic_Doctors()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var directory = new DoctorDirectoryService(
            h.Db,
            new ClinicPublicLookup(h.Db),
            new FakeCurrentUser
            {
                IsAuthenticated = true,
                UserId = Guid.NewGuid(),
                Roles = [AppRoles.PlatformAdmin],
            },
            new FakeCurrentStaff { HasActiveMembership = false },
            new NoOpAuthorizationAuditLogger());

        var doctors = await directory.ListDoctorsByClinicIdAsync(
            data.ClinicAId,
            PlatformAdminBypass.Explicit);

        doctors.Should().Contain(d => d.StaffMemberId == data.DoctorAStaffId);
    }

    [Fact]
    public void Full_Day_Exception_Rejects_Time_Fields()
    {
        var validator = new CreateDoctorAvailabilityExceptionRequestValidator();
        var result = validator.Validate(new CreateDoctorAvailabilityExceptionRequest
        {
            Date = new DateOnly(2026, 8, 1),
            ExceptionType = "UnavailableFullDay",
            StartLocalTime = "09:00",
            EndLocalTime = "10:00",
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Range_Exception_Requires_Start_And_End()
    {
        var validator = new CreateDoctorAvailabilityExceptionRequestValidator();
        var missing = validator.Validate(new CreateDoctorAvailabilityExceptionRequest
        {
            Date = new DateOnly(2026, 8, 1),
            ExceptionType = "UnavailableRange",
        });
        missing.IsValid.Should().BeFalse();

        var valid = validator.Validate(new CreateDoctorAvailabilityExceptionRequest
        {
            Date = new DateOnly(2026, 8, 1),
            ExceptionType = "UnavailableRange",
            StartLocalTime = "09:00",
            EndLocalTime = "11:00",
        });
        valid.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Clinic_Admin_Does_Not_Receive_Medical_Note_Permissions()
    {
        var permissions = RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin);
        permissions.Should().NotContain(Permissions.MedicalNotes.Read);
        permissions.Should().NotContain(Permissions.MedicalNotes.Create);
        permissions.Should().Contain(Permissions.Availability.ManageClinic);
        permissions.Should().Contain(Permissions.Availability.Read);
        permissions.Should().Contain(Permissions.Staff.Read);
    }

    private static void ClearDay(AppointmentHarness h, Guid doctorId, DayOfWeek day)
    {
        var rows = h.Db.DoctorAvailabilities
            .Where(a => a.DoctorStaffMemberId == doctorId && a.DayOfWeek == day)
            .ToList();
        h.Db.DoctorAvailabilities.RemoveRange(rows);
    }

    private static async Task<(Guid UserId, Guid StaffId)> SeedClinicAdminAsync(
        AppointmentHarness h,
        AppointmentHarness.SeedData data)
    {
        var userId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = staffId,
            UserId = userId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.ClinicAdmin,
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();
        return (userId, staffId);
    }
}
