using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.MedicalNotes;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class AppointmentFoundationTests
{
    [Fact]
    public async Task Patient_Creates_Appointment_For_Self()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);

        var created = await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(1),
            DurationMinutes = 30,
            Reason = "Checkup",
        });

        created.PatientId.Should().Be(data.PatientId);
        created.Status.Should().Be(nameof(AppointmentStatus.Requested));
        created.Source.Should().Be(nameof(AppointmentSource.Patient));
        created.ClinicId.Should().Be(data.ClinicAId);
    }

    [Fact]
    public void Create_Patient_Contract_Has_No_PatientId_Or_OrgId()
    {
        typeof(CreatePatientAppointmentRequest).GetProperty("PatientId").Should().BeNull();
        typeof(CreatePatientAppointmentRequest).GetProperty("OrganizationId").Should().BeNull();
        typeof(CreatePatientAppointmentRequest).GetProperty("ClinicId").Should().BeNull();
        typeof(CreatePatientAppointmentRequest).GetProperty("Status").Should().BeNull();
    }

    [Fact]
    public async Task Patient_Must_Be_Enrolled_In_Clinic()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);

        var act = () => sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicBSlug,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(1),
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotEnrolled);
    }

    [Fact]
    public async Task Staff_Creates_Appointment_Within_Clinic()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var created = await sut.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(2),
            DurationMinutes = 30,
        });

        created.Status.Should().Be(nameof(AppointmentStatus.Confirmed));
        created.Source.Should().Be(nameof(AppointmentSource.Staff));
    }

    [Fact]
    public async Task Cross_Clinic_Staff_Booking_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateStaffService(data.DoctorBUserId, data.Org1Id, data.ClinicBId, data.DoctorBStaffId, AppRoles.Doctor);

        var act = () => sut.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(1),
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.NotEnrolled);
    }

    [Fact]
    public async Task Invalid_Assigned_Doctor_Denied()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);

        var act = () => sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(1),
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidAssignedStaff);
    }

    [Fact]
    public async Task Past_Appointment_Time_Rejected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);

        var act = () => sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddHours(-1),
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidTime);
    }

    [Fact]
    public async Task Slot_Conflict_Detected()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var start = h.Now.AddDays(3);

        await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });

        var act = () => sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });

        await act.Should().ThrowAsync<AvailabilityException>()
            .Where(e => e.ErrorCode == AvailabilityErrorCodes.SlotUnavailable);
    }

    [Fact]
    public async Task Cancelled_Appointment_Does_Not_Block_Slot()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var patientSut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var start = h.Now.AddDays(4);

        var first = await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });

        await patientSut.CancelAsync(first.Id, new AppointmentActionRequest { ExpectedVersion = first.Version });

        var second = await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = start,
            DurationMinutes = 30,
        });

        second.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public async Task Patient_Sees_Only_Own_Appointments()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var otherPatient = await h.SeedExtraPatientInClinicAAsync(data);
        var patientSut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var staffSut = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(5),
            DurationMinutes = 30,
        });

        await staffSut.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = otherPatient,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(6),
            DurationMinutes = 30,
        });

        var list = await patientSut.ListForCurrentPatientAsync(new AppointmentListQuery());
        list.Items.Should().OnlyContain(i => i.PatientId == data.PatientId);
    }

    [Fact]
    public async Task Staff_Sees_Only_Clinic_Appointments()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        await h.EnrollPatientInClinicBAsync(data);
        var patientSut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var staffA = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);
        var staffB = h.CreateStaffService(data.DoctorBUserId, data.Org1Id, data.ClinicBId, data.DoctorBStaffId, AppRoles.Doctor);

        await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(7),
            DurationMinutes = 30,
        });

        await staffB.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(8),
            DurationMinutes = 30,
        });

        var listA = await staffA.ListForStaffAsync(new AppointmentListQuery());
        listA.Items.Should().OnlyContain(i => i.ClinicId == data.ClinicAId);
    }

    [Fact]
    public async Task Organization_Admin_Is_Organization_Scoped()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        await h.EnrollPatientInClinicBAsync(data);
        var patientSut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var staffB = h.CreateStaffService(data.DoctorBUserId, data.Org1Id, data.ClinicBId, data.DoctorBStaffId, AppRoles.Doctor);
        var orgAdmin = h.CreateStaffService(Guid.NewGuid(), data.Org1Id, data.ClinicAId, Guid.NewGuid(), AppRoles.OrganizationAdmin);

        await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(9),
            DurationMinutes = 30,
        });
        await staffB.CreateForStaffAsync(new CreateStaffAppointmentRequest
        {
            PatientId = data.PatientId,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = h.Now.AddDays(10),
            DurationMinutes = 30,
        });

        var list = await orgAdmin.ListForStaffAsync(new AppointmentListQuery());
        list.Items.Should().OnlyContain(i => i.OrganizationId == data.Org1Id);
        list.Items.Select(i => i.ClinicId).Should().BeEquivalentTo([data.ClinicAId, data.ClinicBId]);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Explicit_Bypass()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var patientSut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(11),
            DurationMinutes = 30,
        });

        var admin = new AppointmentService(
            h.Db,
            new FakeCurrentUser { IsAuthenticated = true, UserId = Guid.NewGuid(), Roles = [AppRoles.PlatformAdmin] },
            new FakeCurrentStaff(),
            new FakeCurrentPatient(),
            new ClinicPublicLookup(h.Db),
            h.CreateSlots(),
            h.CreateReminderScheduler(),
            h.Time,
            NullLogger<AppointmentService>.Instance);

        var without = () => admin.GetByIdAsync(created.Id);
        await without.Should().ThrowAsync<AppointmentException>();

        var with = await admin.GetByIdAsync(created.Id, PlatformAdminBypass.Explicit);
        with.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task Valid_And_Invalid_Status_Transitions()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var patientSut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var staffSut = h.CreateStaffService(data.DoctorAUserId, data.Org1Id, data.ClinicAId, data.DoctorAStaffId, AppRoles.Doctor);

        var created = await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(12),
            DurationMinutes = 30,
        });

        var confirmed = await staffSut.ConfirmAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = created.Version });
        confirmed.Status.Should().Be(nameof(AppointmentStatus.Confirmed));

        var invalid = () => staffSut.CompleteAsync(confirmed.Id, new AppointmentActionRequest { ExpectedVersion = confirmed.Version });
        await invalid.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.InvalidTransition);

        var checkedIn = await staffSut.CheckInAsync(confirmed.Id, new AppointmentActionRequest { ExpectedVersion = confirmed.Version });
        var completed = await staffSut.CompleteAsync(checkedIn.Id, new AppointmentActionRequest { ExpectedVersion = checkedIn.Version });
        completed.Status.Should().Be(nameof(AppointmentStatus.Completed));
    }

    [Fact]
    public async Task Concurrency_Conflict_On_Transition()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var patientSut = h.CreatePatientService(data.PatientUserId, data.PatientId);
        var created = await patientSut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
        {
            ClinicCode = data.ClinicASlug,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = h.Now.AddDays(13),
            DurationMinutes = 30,
        });

        var act = () => patientSut.CancelAsync(created.Id, new AppointmentActionRequest { ExpectedVersion = 99 });
        await act.Should().ThrowAsync<AppointmentException>()
            .Where(e => e.ErrorCode == AppointmentErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Pagination_And_Filters_Work()
    {
        await using var h = await AppointmentHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreatePatientService(data.PatientUserId, data.PatientId);

        for (var i = 0; i < 3; i++)
        {
            await sut.CreateForCurrentPatientAsync(new CreatePatientAppointmentRequest
            {
                ClinicCode = data.ClinicASlug,
                DoctorStaffMemberId = data.DoctorAStaffId,
                AppointmentDateUtc = h.Now.AddDays(20 + i),
                DurationMinutes = 30,
            });
        }

        var page = await sut.ListForCurrentPatientAsync(new AppointmentListQuery { Page = 1, PageSize = 2 });
        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(3);
        page.TotalPages.Should().Be(2);
    }

    [Fact]
    public void Status_Transition_Matrix_Is_Enforced()
    {
        AppointmentStatusTransitions.CanTransition(AppointmentStatus.Requested, AppointmentStatus.Confirmed).Should().BeTrue();
        AppointmentStatusTransitions.CanTransition(AppointmentStatus.Completed, AppointmentStatus.CancelledByPatient).Should().BeFalse();
        AppointmentStatusTransitions.CanTransition(AppointmentStatus.CancelledByClinic, AppointmentStatus.Confirmed).Should().BeFalse();
    }
}

internal sealed class AppointmentHarness : IAsyncDisposable
{
    private AppointmentHarness(HealthCareDbContext db, TimeProvider time)
    {
        Db = db;
        Time = time;
    }

    public HealthCareDbContext Db { get; }

    public TimeProvider Time { get; }

    public DateTimeOffset Now => Time.GetUtcNow();

    public static async Task<AppointmentHarness> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new HealthCareDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-23T12:00:00Z"));
        return new AppointmentHarness(db, time);
    }

    public async Task<SeedData> SeedAsync()
    {
        var org1 = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var patientUserId = Guid.NewGuid();
        var doctorAUser = Guid.NewGuid();
        var doctorBUser = Guid.NewGuid();
        var doctorAStaff = Guid.NewGuid();
        var doctorBStaff = Guid.NewGuid();
        const string slugA = "appt-clinic-a";
        const string slugB = "appt-clinic-b";

        Db.Organizations.Add(new Organization
        {
            Id = org1,
            Name = "Org",
            Slug = "org-appt",
            Status = OrganizationStatus.Active,
        });
        Db.Clinics.AddRange(
            new Domain.Clinics.Clinic
            {
                Id = clinicA,
                OrganizationId = org1,
                Name = "A",
                Slug = slugA,
                TimeZoneId = "Asia/Riyadh",
                IsActive = true,
            },
            new Domain.Clinics.Clinic
            {
                Id = clinicB,
                OrganizationId = org1,
                Name = "B",
                Slug = slugB,
                TimeZoneId = "Asia/Riyadh",
                IsActive = true,
            });
        Db.Patients.Add(new Patient
        {
            Id = patientId,
            UserId = patientUserId,
            FirstName = "Pat",
            LastName = "Ent",
            IsActive = true,
        });
        Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicA,
            PatientId = patientId,
            LocalPatientNumber = "A-1",
            Status = ClinicPatientStatus.Active,
            RegisteredAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        Db.StaffMembers.AddRange(
            new StaffMember
            {
                Id = doctorAStaff,
                UserId = doctorAUser,
                OrganizationId = org1,
                ClinicId = clinicA,
                Role = AppRoles.Doctor,
                IsActive = true,
            },
            new StaffMember
            {
                Id = doctorBStaff,
                UserId = doctorBUser,
                OrganizationId = org1,
                ClinicId = clinicB,
                Role = AppRoles.Doctor,
                IsActive = true,
            });

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            Db.DoctorAvailabilities.AddRange(
                new DoctorAvailability
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = org1,
                    ClinicId = clinicA,
                    DoctorStaffMemberId = doctorAStaff,
                    DayOfWeek = day,
                    StartLocalTime = new TimeOnly(8, 0),
                    EndLocalTime = new TimeOnly(20, 0),
                    SlotDurationMinutes = 30,
                    EffectiveFrom = new DateOnly(2020, 1, 1),
                    IsActive = true,
                },
                new DoctorAvailability
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = org1,
                    ClinicId = clinicB,
                    DoctorStaffMemberId = doctorBStaff,
                    DayOfWeek = day,
                    StartLocalTime = new TimeOnly(8, 0),
                    EndLocalTime = new TimeOnly(20, 0),
                    SlotDurationMinutes = 30,
                    EffectiveFrom = new DateOnly(2020, 1, 1),
                    IsActive = true,
                });
        }

        await Db.SaveChangesAsync();

        return new SeedData(
            org1, clinicA, clinicB, slugA, slugB, patientId, patientUserId,
            doctorAUser, doctorBUser, doctorAStaff, doctorBStaff);
    }

    public async Task EnrollPatientInClinicBAsync(SeedData data)
    {
        Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = data.ClinicBId,
            PatientId = data.PatientId,
            LocalPatientNumber = "B-1",
            Status = ClinicPatientStatus.Active,
            RegisteredAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        await Db.SaveChangesAsync();
    }

    public async Task<Guid> SeedExtraPatientInClinicAAsync(SeedData data)
    {
        var patientId = Guid.NewGuid();
        Db.Patients.Add(new Patient
        {
            Id = patientId,
            FirstName = "Other",
            LastName = "Patient",
            IsActive = true,
        });
        Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = data.ClinicAId,
            PatientId = patientId,
            LocalPatientNumber = "A-2",
            Status = ClinicPatientStatus.Active,
            RegisteredAtUtc = Now,
            UpdatedAtUtc = Now,
        });
        await Db.SaveChangesAsync();
        return patientId;
    }

    public AppointmentService CreatePatientService(Guid userId, Guid patientId)
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = [AppRoles.Patient],
            PatientId = patientId,
        };
        var patient = new FakeCurrentPatient { HasLinkedPatient = true, PatientId = patientId };
        return new AppointmentService(
            Db, user, new FakeCurrentStaff(), patient, new ClinicPublicLookup(Db), CreateSlots(),
            CreateReminderScheduler(), Time,
            NullLogger<AppointmentService>.Instance);
    }

    public AppointmentService CreateStaffService(
        Guid userId,
        Guid orgId,
        Guid clinicId,
        Guid staffMemberId,
        string role)
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = [role],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = staffMemberId,
            OrganizationId = orgId,
            ClinicId = clinicId,
            Role = role,
        };
        return new AppointmentService(
            Db, user, staff, new FakeCurrentPatient(), new ClinicPublicLookup(Db), CreateSlots(),
            CreateReminderScheduler(), Time,
            NullLogger<AppointmentService>.Instance);
    }

    public AppointmentSlotService CreateSlots() =>
        new(
            Db,
            new ClinicPublicLookup(Db),
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Time,
            NullLogger<AppointmentSlotService>.Instance);

    public ImmediateReminderBackgroundJobs Jobs { get; } = new();

    public AppointmentReminderScheduler CreateReminderScheduler() =>
        new(Db, Jobs, Time, NullLogger<AppointmentReminderScheduler>.Instance);

    public AppointmentReminderProcessor CreateReminderProcessor(IAppointmentReminderSender? sender = null) =>
        new(
            Db,
            sender ?? new DevelopmentAppointmentReminderSender(NullLogger<DevelopmentAppointmentReminderSender>.Instance),
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Time,
            NullLogger<AppointmentReminderProcessor>.Instance);

    public AppointmentReminderRecoveryService CreateRecovery() =>
        new(Db, Jobs, Time, NullLogger<AppointmentReminderRecoveryService>.Instance);

    public AppointmentReminderService CreateReminderService(
        Guid userId,
        Guid orgId,
        Guid clinicId,
        Guid staffMemberId,
        string role,
        bool isPatient = false)
    {
        var roles = isPatient ? new[] { AppRoles.Patient } : new[] { role };
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = roles,
        };
        var staff = isPatient
            ? new FakeCurrentStaff()
            : new FakeCurrentStaff
            {
                HasActiveMembership = true,
                StaffMemberId = staffMemberId,
                OrganizationId = orgId,
                ClinicId = clinicId,
                Role = role,
            };
        return new AppointmentReminderService(
            Db, user, staff, Jobs, Time, NullLogger<AppointmentReminderService>.Instance);
    }

    public DoctorAvailabilityService CreateAvailabilityService(
        Guid userId,
        Guid orgId,
        Guid clinicId,
        Guid staffMemberId,
        string role,
        bool isPatient = false)
    {
        var roles = isPatient ? new[] { AppRoles.Patient } : new[] { role };
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = roles,
            PatientId = isPatient ? Guid.NewGuid() : null,
        };
        var staff = isPatient
            ? new FakeCurrentStaff()
            : new FakeCurrentStaff
            {
                HasActiveMembership = true,
                StaffMemberId = staffMemberId,
                OrganizationId = orgId,
                ClinicId = clinicId,
                Role = role,
            };
        return new DoctorAvailabilityService(
            Db, user, staff, NullLogger<DoctorAvailabilityService>.Instance);
    }

    public DoctorDirectoryService CreateDirectory() => new(Db, new ClinicPublicLookup(Db));

    public MedicalNoteService CreateMedicalNoteService(
        Guid userId,
        Guid organizationId,
        Guid clinicId,
        Guid staffMemberId,
        string role)
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userId,
            Roles = [role],
        };
        var staff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = staffMemberId,
            OrganizationId = organizationId,
            ClinicId = clinicId,
            Role = role,
        };
        var access = new MedicalNoteAccessService(user, staff, new NoOpAuthorizationAuditLogger());
        var audit = new MedicalNoteAuditStore(
            Db, user, staff, Time, NullLogger<MedicalNoteAuditStore>.Instance);

        return new MedicalNoteService(
            Db, user, staff, access, audit, Time, NullLogger<MedicalNoteService>.Instance);
    }

    public async Task<Appointment> SeedAppointmentAsync(
        SeedData data,
        AppointmentStatus status = AppointmentStatus.CheckedIn)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            PatientId = data.PatientId,
            ClinicPatientId = await Db.ClinicPatients
                .Where(cp => cp.ClinicId == data.ClinicAId && cp.PatientId == data.PatientId)
                .Select(cp => cp.Id)
                .SingleAsync(),
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = Now,
            DurationMinutes = 30,
            Status = status,
            Source = AppointmentSource.Staff,
            CreatedByUserId = data.DoctorAUserId,
            Version = 0,
            CreatedAtUtc = Now,
            UpdatedAtUtc = Now,
        };
        Db.Appointments.Add(appointment);
        await Db.SaveChangesAsync();
        return appointment;
    }

    public ValueTask DisposeAsync()
    {
        Db.Dispose();
        return ValueTask.CompletedTask;
    }

    public sealed record SeedData(
        Guid Org1Id,
        Guid ClinicAId,
        Guid ClinicBId,
        string ClinicASlug,
        string ClinicBSlug,
        Guid PatientId,
        Guid PatientUserId,
        Guid DoctorAUserId,
        Guid DoctorBUserId,
        Guid DoctorAStaffId,
        Guid DoctorBStaffId);
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}
