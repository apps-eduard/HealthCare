using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class AppointmentAvailabilityTests
{
    [Fact]
    public async Task Active_Clinic_Doctors_Listed()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var doctors = await h.CreateDirectory().ListDoctorsByClinicCodeAsync(data.ClinicASlug);

        doctors.Should().ContainSingle(d => d.StaffMemberId == data.DoctorAStaffId);
        doctors.Single().AcceptsBookings.Should().BeTrue();
        doctors.Single().ClinicCode.Should().Be(data.ClinicASlug);
    }

    [Fact]
    public async Task Non_Doctor_Staff_Excluded()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.ClinicAdmin,
            IsActive = true,
            JobTitle = "Admin",
        });
        await h.Db.SaveChangesAsync();

        var doctors = await h.CreateDirectory().ListDoctorsByClinicCodeAsync(data.ClinicASlug);
        doctors.Should().OnlyContain(d => d.StaffMemberId == data.DoctorAStaffId);
    }

    [Fact]
    public async Task Inactive_Doctor_Excluded()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var doctor = await h.Db.StaffMembers.SingleAsync(s => s.Id == data.DoctorAStaffId);
        doctor.IsActive = false;
        await h.Db.SaveChangesAsync();

        var doctors = await h.CreateDirectory().ListDoctorsByClinicCodeAsync(data.ClinicASlug);
        doctors.Should().BeEmpty();
    }

    [Fact]
    public async Task Doctor_From_Another_Clinic_Excluded()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var doctors = await h.CreateDirectory().ListDoctorsByClinicCodeAsync(data.ClinicASlug);
        doctors.Should().NotContain(d => d.StaffMemberId == data.DoctorBStaffId);
    }

    [Fact]
    public async Task Valid_Weekly_Availability_Created()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        // Clear seeded windows for a clean create on Monday night (outside seeded range would conflict).
        // Create an evening window that does not overlap 08:00-20:00 — use inactive day after clearing one day.
        var monday = await h.Db.DoctorAvailabilities
            .Where(a => a.DoctorStaffMemberId == data.DoctorAStaffId && a.DayOfWeek == DayOfWeek.Monday)
            .ToListAsync();
        h.Db.DoctorAvailabilities.RemoveRange(monday);
        await h.Db.SaveChangesAsync();

        var created = await sut.CreateAvailabilityAsync(data.DoctorAStaffId, new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = nameof(DayOfWeek.Monday),
            StartLocalTime = "09:00",
            EndLocalTime = "12:00",
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

        created.StartLocalTime.Should().Be("09:00");
        created.SlotDurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task Invalid_Time_Range_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        var act = () => sut.CreateAvailabilityAsync(data.DoctorAStaffId, new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = nameof(DayOfWeek.Tuesday),
            StartLocalTime = "16:00",
            EndLocalTime = "10:00",
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.InvalidAvailability);
    }

    [Fact]
    public async Task Overlapping_Availability_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        var act = () => sut.CreateAvailabilityAsync(data.DoctorAStaffId, new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = nameof(DayOfWeek.Wednesday),
            StartLocalTime = "10:00",
            EndLocalTime = "11:00",
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2020, 1, 1),
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.AvailabilityConflict);
    }

    [Fact]
    public async Task Invalid_Slot_Duration_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        ClearDay(h, data.DoctorAStaffId, DayOfWeek.Thursday);
        await h.Db.SaveChangesAsync();

        var act = () => sut.CreateAvailabilityAsync(data.DoctorAStaffId, new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = nameof(DayOfWeek.Thursday),
            StartLocalTime = "09:00",
            EndLocalTime = "12:00",
            SlotDurationMinutes = 5,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.InvalidAvailability);
    }

    [Fact]
    public async Task Availability_Exception_Blocks_Slots()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var date = DateOnly.FromDateTime(h.Now.AddDays(1).UtcDateTime);
        h.Db.DoctorAvailabilityExceptions.Add(new DoctorAvailabilityException
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            Date = date,
            ExceptionType = AvailabilityExceptionType.UnavailableRange,
            StartLocalTime = new TimeOnly(14, 0),
            EndLocalTime = new TimeOnly(16, 0),
        });
        await h.Db.SaveChangesAsync();

        var slots = await h.CreateSlots().GetAvailableSlotsAsync(
            data.ClinicASlug,
            data.DoctorAStaffId,
            new AvailableSlotsQuery { Date = date, DurationMinutes = 30 });

        slots.Should().NotContain(s =>
            TimeOnly.FromDateTime(DateTimeOffset.Parse(s.StartLocal).DateTime) >= new TimeOnly(14, 0)
            && TimeOnly.FromDateTime(DateTimeOffset.Parse(s.StartLocal).DateTime) < new TimeOnly(16, 0));
    }

    [Fact]
    public async Task Full_Day_Exception_Blocks_All_Slots()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var date = DateOnly.FromDateTime(h.Now.AddDays(1).UtcDateTime);
        h.Db.DoctorAvailabilityExceptions.Add(new DoctorAvailabilityException
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            Date = date,
            ExceptionType = AvailabilityExceptionType.UnavailableFullDay,
        });
        await h.Db.SaveChangesAsync();

        var slots = await h.CreateSlots().GetAvailableSlotsAsync(
            data.ClinicASlug,
            data.DoctorAStaffId,
            new AvailableSlotsQuery { Date = date });

        slots.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_Appointment_Removes_Occupied_Slot()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var start = h.Now.AddDays(1);
        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        await patient.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });

        var date = DateOnly.FromDateTime(start.UtcDateTime);
        var slots = await h.CreateSlots().GetAvailableSlotsAsync(
            data.ClinicASlug,
            data.DoctorAStaffId,
            new AvailableSlotsQuery { Date = date, DurationMinutes = 30 });

        slots.Should().NotContain(s => s.StartUtc == start);
    }

    [Fact]
    public async Task Cancelled_Appointment_Does_Not_Block_Slot()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var start = h.Now.AddDays(1);
        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await patient.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });

        await patient.CancelAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });

        var date = DateOnly.FromDateTime(start.UtcDateTime);
        var slots = await h.CreateSlots().GetAvailableSlotsAsync(
            data.ClinicASlug,
            data.DoctorAStaffId,
            new AvailableSlotsQuery { Date = date, DurationMinutes = 30 });

        slots.Should().Contain(s => s.StartUtc == start);
    }

    [Fact]
    public async Task Past_Slots_Excluded()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        // Fixed now is 12:00 UTC = 15:00 Asia/Riyadh on 2026-07-23
        var today = DateOnly.FromDateTime(h.Now.UtcDateTime);
        var slots = await h.CreateSlots().GetAvailableSlotsAsync(
            data.ClinicASlug,
            data.DoctorAStaffId,
            new AvailableSlotsQuery { Date = today, DurationMinutes = 30 });

        slots.Should().OnlyContain(s => s.StartUtc > h.Now);
        slots.Should().NotContain(s =>
            TimeOnly.FromDateTime(DateTimeOffset.Parse(s.StartLocal).DateTime) < new TimeOnly(15, 0));
    }

    [Fact]
    public void Slot_Boundary_Enforced()
    {
        AvailabilitySlotRules.IsOnSlotBoundary(new TimeOnly(9, 0), new TimeOnly(8, 0), 30).Should().BeTrue();
        AvailabilitySlotRules.IsOnSlotBoundary(new TimeOnly(9, 15), new TimeOnly(8, 0), 30).Should().BeFalse();
    }

    [Fact]
    public async Task Appointment_Outside_Availability_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        ClearDay(h, data.DoctorAStaffId, DayOfWeek.Friday);
        await h.Db.SaveChangesAsync();

        // 2026-07-24 is Friday
        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var act = () => patient.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(1),
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.OutsideAvailability);
    }

    [Fact]
    public async Task Appointment_Crossing_Window_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        ClearDay(h, data.DoctorAStaffId, DayOfWeek.Friday);
        h.Db.DoctorAvailabilities.Add(new DoctorAvailability
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            DayOfWeek = DayOfWeek.Friday,
            StartLocalTime = new TimeOnly(14, 0),
            EndLocalTime = new TimeOnly(15, 30),
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        // Local 15:00 + 30 min = 15:30 fits; use 15:00 which fits. Instead book at 15:00 with duration that would need longer window - use boundary at end.
        // Start 15:00 local = 12:00 UTC fits exactly ending at 15:30. Create shorter end so 15:00 doesn't fit: end 15:15.
        var window = await h.Db.DoctorAvailabilities.SingleAsync(
            a => a.DoctorStaffMemberId == data.DoctorAStaffId && a.DayOfWeek == DayOfWeek.Friday);
        window.EndLocalTime = new TimeOnly(15, 15);
        await h.Db.SaveChangesAsync();

        var patient = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var act = () => patient.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(1), // 15:00 local
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AvailabilityException>();
    }

    [Fact]
    public void Clinic_Timezone_Conversion_Correct()
    {
        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var utc = converter.ToUtc(new DateOnly(2026, 7, 24), new TimeOnly(15, 0), "Asia/Riyadh");
        utc.Should().Be(DateTimeOffset.Parse("2026-07-24T12:00:00Z"));

        converter.GetClinicTime(utc, "Asia/Riyadh").Should().Be(new TimeOnly(15, 0));
        converter.GetClinicDate(utc, "Asia/Riyadh").Should().Be(new DateOnly(2026, 7, 24));
    }

    [Fact]
    public async Task Staff_Cannot_Manage_Another_Clinic_Doctor()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var adminB = await SeedClinicAdminAsync(h, data, forClinicB: true);
        var sut = h.CreateAvailabilityService(
            adminB.UserId, data.Org1Id, data.ClinicBId, adminB.StaffId, AppRoles.ClinicAdmin);

        var act = () => sut.ListAvailabilityAsync(data.DoctorAStaffId);
        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.DoctorNotFound);
    }

    [Fact]
    public async Task Organization_Admin_Remains_Organization_Scoped()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var orgAdminUser = Guid.NewGuid();
        var orgAdminStaff = Guid.NewGuid();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = orgAdminStaff,
            UserId = orgAdminUser,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.OrganizationAdmin,
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAvailabilityService(
            orgAdminUser, data.Org1Id, data.ClinicAId, orgAdminStaff, AppRoles.OrganizationAdmin);

        var list = await sut.ListAvailabilityAsync(data.DoctorBStaffId, clinicId: data.ClinicBId);
        list.Should().NotBeEmpty();
        list[0].ClinicTimeZoneId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Manage_Other_Organization_Doctor()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var orgAdminUser = Guid.NewGuid();
        var orgAdminStaff = Guid.NewGuid();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = orgAdminStaff,
            UserId = orgAdminUser,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.OrganizationAdmin,
            IsActive = true,
        });

        var org2 = Guid.NewGuid();
        var clinicOther = Guid.NewGuid();
        var foreignDoctor = Guid.NewGuid();
        h.Db.Organizations.Add(new Organization
        {
            Id = org2,
            Name = "Other Org",
            Slug = "other-org-avail",
            Status = OrganizationStatus.Active,
        });
        h.Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinicOther,
            OrganizationId = org2,
            Name = "Other Clinic",
            Slug = "other-clinic-avail",
            TimeZoneId = "Asia/Riyadh",
            IsActive = true,
        });
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = foreignDoctor,
            UserId = Guid.NewGuid(),
            OrganizationId = org2,
            ClinicId = clinicOther,
            Role = AppRoles.Doctor,
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAvailabilityService(
            orgAdminUser, data.Org1Id, data.ClinicAId, orgAdminStaff, AppRoles.OrganizationAdmin);

        var act = () => sut.ListAvailabilityAsync(foreignDoctor);
        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.DoctorNotFound);
    }

    [Fact]
    public async Task Organization_Admin_ClinicId_Must_Match_Doctor_Clinic()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var orgAdminUser = Guid.NewGuid();
        var orgAdminStaff = Guid.NewGuid();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = orgAdminStaff,
            UserId = orgAdminUser,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.OrganizationAdmin,
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAvailabilityService(
            orgAdminUser, data.Org1Id, data.ClinicAId, orgAdminStaff, AppRoles.OrganizationAdmin);

        var mismatch = () => sut.ListAvailabilityAsync(data.DoctorAStaffId, clinicId: data.ClinicBId);
        await mismatch.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.DoctorNotFound);

        var doctors = await h.CreateDirectory(
                orgAdminUser, data.Org1Id, data.ClinicAId, orgAdminStaff, AppRoles.OrganizationAdmin)
            .ListDoctorsByClinicIdAsync(data.ClinicBId);
        doctors.Should().Contain(d => d.StaffMemberId == data.DoctorBStaffId);
        doctors.Should().OnlyContain(d => d.ClinicId == data.ClinicBId);

        var denyClinic = () => h.CreateDirectory(
                orgAdminUser, data.Org1Id, data.ClinicAId, orgAdminStaff, AppRoles.OrganizationAdmin)
            .ListDoctorsByClinicIdAsync(Guid.NewGuid());
        await denyClinic.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Organization_Admin_Can_Update_Window_And_Create_Exception_For_Sibling_Clinic_Doctor()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var orgAdminUser = Guid.NewGuid();
        var orgAdminStaff = Guid.NewGuid();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = orgAdminStaff,
            UserId = orgAdminUser,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.OrganizationAdmin,
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAvailabilityService(
            orgAdminUser, data.Org1Id, data.ClinicAId, orgAdminStaff, AppRoles.OrganizationAdmin);

        var existing = (await sut.ListAvailabilityAsync(data.DoctorBStaffId, clinicId: data.ClinicBId))
            .First(w => w.DayOfWeek == "Wednesday");

        var updated = await sut.UpdateAvailabilityAsync(
            data.DoctorBStaffId,
            existing.Id,
            new UpdateDoctorAvailabilityRequest
            {
                ExpectedVersion = existing.Version,
                EndLocalTime = "18:00",
            },
            clinicId: data.ClinicBId);

        updated.ClinicId.Should().Be(data.ClinicBId);
        updated.EndLocalTime.Should().Be("18:00");
        updated.ClinicTimeZoneId.Should().NotBeNullOrWhiteSpace();

        var exception = await sut.CreateExceptionAsync(
            data.DoctorBStaffId,
            new CreateDoctorAvailabilityExceptionRequest
            {
                Date = new DateOnly(2026, 9, 2),
                ExceptionType = "UnavailableFullDay",
                Reason = "Training",
            },
            clinicId: data.ClinicBId);
        exception.ExceptionType.Should().Be("UnavailableFullDay");
    }

    [Fact]
    public async Task Patient_Cannot_Manage_Availability()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateAvailabilityService(
            data.PatientUserId, data.Org1Id, data.ClinicAId, Guid.Empty, AppRoles.Patient, isPatient: true);

        var act = () => sut.ListAvailabilityAsync(data.DoctorAStaffId);
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Doctor_May_Manage_Own_Availability()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateAvailabilityService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var list = await sut.ListAvailabilityAsync(data.DoctorAStaffId);
        list.Should().NotBeEmpty();
    }

    [Fact]
    public async Task List_Exceptions_Returns_Doctor_Exceptions()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateAvailabilityService(
            data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var date = new DateOnly(2026, 8, 15);
        await sut.CreateExceptionAsync(data.DoctorAStaffId, new CreateDoctorAvailabilityExceptionRequest
        {
            Date = date,
            ExceptionType = "UnavailableFullDay",
            Reason = "Conference",
        });

        var list = await sut.ListExceptionsAsync(data.DoctorAStaffId);
        list.Should().Contain(e => e.Date == date && e.ExceptionType == "UnavailableFullDay");
    }

    [Fact]
    public async Task Stale_Availability_Version_Returns_Conflict()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        var existing = (await sut.ListAvailabilityAsync(data.DoctorAStaffId)).First();
        var act = () => sut.UpdateAvailabilityAsync(data.DoctorAStaffId, existing.Id, new UpdateDoctorAvailabilityRequest
        {
            ExpectedVersion = existing.Version + 5,
            IsActive = false,
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.AvailabilityConcurrency);
    }

    [Fact]
    public async Task Client_Tenant_Identifiers_Cannot_Bypass_Trusted_Scope()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await SeedClinicAdminAsync(h, data);
        var sut = h.CreateAvailabilityService(
            admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.ClinicAdmin);

        ClearDay(h, data.DoctorAStaffId, DayOfWeek.Saturday);
        await h.Db.SaveChangesAsync();

        var created = await sut.CreateAvailabilityAsync(data.DoctorAStaffId, new CreateDoctorAvailabilityRequest
        {
            DayOfWeek = nameof(DayOfWeek.Saturday),
            StartLocalTime = "09:00",
            EndLocalTime = "10:00",
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2026, 1, 1),
        });

        var entity = await h.Db.DoctorAvailabilities.SingleAsync(a => a.Id == created.Id);
        entity.ClinicId.Should().Be(data.ClinicAId);
        entity.OrganizationId.Should().Be(data.Org1Id);
        created.ClinicId.Should().Be(data.ClinicAId);
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
        AppointmentHarness.SeedData data,
        bool forClinicB = false)
    {
        var userId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        h.Db.StaffMembers.Add(new StaffMember
        {
            Id = staffId,
            UserId = userId,
            OrganizationId = data.Org1Id,
            ClinicId = forClinicB ? data.ClinicBId : data.ClinicAId,
            Role = AppRoles.ClinicAdmin,
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();
        return (userId, staffId);
    }
}
