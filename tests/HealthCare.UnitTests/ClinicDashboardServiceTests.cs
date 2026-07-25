using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class ClinicDashboardServiceTests
{
    [Fact]
    public void Clinic_Dashboard_Permission_Is_Granted_To_Clinic_And_Platform_Admin_Only()
    {
        Permissions.All.Should().Contain(Permissions.Clinics.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().Contain(Permissions.Clinics.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Clinics.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().NotContain(Permissions.Clinics.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor)
            .Should().NotContain(Permissions.Clinics.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Nurse)
            .Should().NotContain(Permissions.Clinics.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Receptionist)
            .Should().NotContain(Permissions.Clinics.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Patient)
            .Should().NotContain(Permissions.Clinics.DashboardRead);
    }

    [Fact]
    public async Task Clinic_Admin_Sees_Own_Clinic_Aggregates_Only()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-dash@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-a@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-b@test.local");
        await h.SeedPatientEnrollmentAsync(h.ClinicA.Id, active: true);
        await h.SeedPatientEnrollmentAsync(h.ClinicB.Id, active: true);
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);
        await h.SeedAppointmentAsync(h.ClinicB.Id, AppointmentStatus.Requested);

        var sut = h.CreateService(clinicAdmin);
        var result = await sut.GetAsync(new ClinicDashboardQuery());

        result.ClinicId.Should().Be(h.ClinicA.Id);
        result.ClinicName.Should().Be(h.ClinicA.Name);
        result.OrganizationName.Should().Be(h.Org.Name);
        result.DefaultTimeZoneId.Should().Be(h.ClinicA.TimeZoneId);
        result.ActiveDoctorCount.Should().Be(1);
        result.ActivePatientCount.Should().BeGreaterThanOrEqualTo(1);
        result.TodayAppointmentCount.Should().Be(1);
        result.TodayAppointmentsByStatus.ConfirmedCount.Should().Be(1);
        result.MonthlyAppointmentCount.Should().BeGreaterThanOrEqualTo(1);
        result.TimeZoneStrategy.Should().Be("clinic");

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().NotContain("MaxClinics");
        json.Should().NotContain("MaxStaff");
        json.Should().NotContain("Remaining");
        json.ToLowerInvariant().Should().NotContain("billing");
        json.ToLowerInvariant().Should().NotContain("subscription");
    }

    [Fact]
    public async Task Clinic_Admin_Cannot_Select_Another_Clinic()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-scope@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.GetAsync(new ClinicDashboardQuery { ClinicId = h.ClinicB.Id });
        await act.Should().ThrowAsync<ClinicDashboardException>()
            .Where(e => e.ErrorCode == ClinicDashboardErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Organization_Admin_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-clinic-dash@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.GetAsync(new ClinicDashboardQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_ClinicId()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("plat-clinic-dash@test.local");
        var sut = h.CreatePlatformService(platform);

        var withoutBypass = () => sut.GetAsync(new ClinicDashboardQuery { ClinicId = h.ClinicA.Id });
        await withoutBypass.Should().ThrowAsync<AuthorizationException>();

        var withoutClinic = () => sut.GetAsync(new ClinicDashboardQuery(), PlatformAdminBypass.Explicit);
        await withoutClinic.Should().ThrowAsync<ClinicDashboardException>()
            .Where(e => e.ErrorCode == ClinicDashboardErrorCodes.ClinicScopeRequired);

        var invalidClinic = () => sut.GetAsync(
            new ClinicDashboardQuery { ClinicId = Guid.NewGuid() },
            PlatformAdminBypass.Explicit);
        await invalidClinic.Should().ThrowAsync<ClinicDashboardException>()
            .Where(e => e.ErrorCode == ClinicDashboardErrorCodes.ClinicNotFound);

        var ok = await sut.GetAsync(
            new ClinicDashboardQuery { ClinicId = h.ClinicA.Id },
            PlatformAdminBypass.Explicit);
        ok.ClinicId.Should().Be(h.ClinicA.Id);
    }

    [Fact]
    public async Task Inactive_Membership_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-inactive@test.local");
        clinicAdmin.Staff.IsActive = false;
        await h.Db.SaveChangesAsync();

        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = clinicAdmin.User.Id,
            Email = clinicAdmin.User.Email,
            Roles = [AppRoles.ClinicAdmin],
            OrganizationId = clinicAdmin.Staff.OrganizationId,
            ClinicId = clinicAdmin.Staff.ClinicId,
            StaffMemberId = clinicAdmin.Staff.Id,
        };
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        var sut = h.BuildService(currentUser, currentStaff);

        var act = () => sut.GetAsync(new ClinicDashboardQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Date_Boundaries_Use_Clinic_Timezone()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-tz@test.local");
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);

        var sut = h.CreateService(clinicAdmin);
        var result = await sut.GetAsync(new ClinicDashboardQuery());

        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var expectedDate = converter.GetClinicDate(h.Clock.GetUtcNow(), h.ClinicA.TimeZoneId).ToString("yyyy-MM-dd");
        result.DashboardDate.Should().Be(expectedDate);
        result.DefaultTimeZoneId.Should().Be("Asia/Riyadh");
    }
}

internal sealed class ClinicDashHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;

    public HealthCareDbContext Db { get; private set; } = null!;

    public UserManager<ApplicationUser> Users { get; private set; } = null!;

    public Organization Org { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicA { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicB { get; private set; } = null!;

    public TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));

    public static async Task<ClinicDashHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>();
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase("clinic-dash-" + Guid.NewGuid().ToString("N")));

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<HealthCareDbContext>();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>
                {
                    Name = role,
                    NormalizedName = role.ToUpperInvariant(),
                });
            }
        }

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Clinic Dash Org",
            Slug = "clinic-dash-org",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic A",
            Slug = "clinic-a-dash",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicB = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic B",
            Slug = "clinic-b-dash",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new ClinicDashHarness
        {
            _provider = provider,
            Db = db,
            Users = users,
            Org = org,
            ClinicA = clinicA,
            ClinicB = clinicB,
        };
    }

    public async Task<(ApplicationUser User, Domain.Staff.StaffMember Staff)> SeedStaffAsync(
        string role,
        Guid clinicId,
        string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        (await Users.CreateAsync(user, "TempPass_Staff_99!")).Succeeded.Should().BeTrue();
        (await Users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        var staff = new Domain.Staff.StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrganizationId = Org.Id,
            ClinicId = clinicId,
            Role = role,
            FirstName = "Test",
            LastName = role,
            IsActive = true,
            Version = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        Db.StaffMembers.Add(staff);
        await Db.SaveChangesAsync();
        return (user, staff);
    }

    public async Task<ApplicationUser> SeedPlatformAdminAsync(string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        (await Users.CreateAsync(user, "TempPass_Staff_99!")).Succeeded.Should().BeTrue();
        (await Users.AddToRoleAsync(user, AppRoles.PlatformAdmin)).Succeeded.Should().BeTrue();
        return user;
    }

    public async Task SeedPatientEnrollmentAsync(Guid clinicId, bool active)
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FirstName = "Pat",
            LastName = "Ent",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        Db.Patients.Add(patient);
        Db.ClinicPatients.Add(new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patient.Id,
            LocalPatientNumber = "P-" + Guid.NewGuid().ToString("N")[..8],
            Status = active ? ClinicPatientStatus.Active : ClinicPatientStatus.Inactive,
            Version = 0,
            RegisteredAtUtc = Clock.GetUtcNow(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    public async Task SeedAppointmentAsync(Guid clinicId, AppointmentStatus status)
    {
        var doctor = await Db.StaffMembers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Role == AppRoles.Doctor);
        if (doctor is null)
        {
            var seeded = await SeedStaffAsync(AppRoles.Doctor, clinicId, $"auto-doc-{Guid.NewGuid():N}@test.local");
            doctor = seeded.Staff;
        }

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FirstName = "Appt",
            LastName = "Patient",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicPatient = new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patient.Id,
            LocalPatientNumber = "P-" + Guid.NewGuid().ToString("N")[..8],
            Status = ClinicPatientStatus.Active,
            Version = 0,
            RegisteredAtUtc = Clock.GetUtcNow(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var localDate = converter.GetClinicDate(Clock.GetUtcNow(), "Asia/Riyadh");
        var startUtc = converter.ToUtc(localDate, new TimeOnly(10, 0), "Asia/Riyadh");

        Db.Patients.Add(patient);
        Db.ClinicPatients.Add(clinicPatient);
        Db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = Org.Id,
            ClinicId = clinicId,
            PatientId = patient.Id,
            ClinicPatientId = clinicPatient.Id,
            DoctorStaffMemberId = doctor.Id,
            AppointmentDateUtc = startUtc,
            DurationMinutes = 30,
            Status = status,
            Source = AppointmentSource.Staff,
            CreatedByUserId = doctor.UserId,
            Version = 0,
            CreatedAtUtc = Clock.GetUtcNow(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    public ClinicDashboardService CreateService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
    {
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = actor.User.Id,
            Email = actor.User.Email,
            Roles = [actor.Staff.Role],
            OrganizationId = actor.Staff.OrganizationId,
            ClinicId = actor.Staff.ClinicId,
            StaffMemberId = actor.Staff.Id,
        };
        var currentStaff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = actor.Staff.Id,
            OrganizationId = actor.Staff.OrganizationId,
            ClinicId = actor.Staff.ClinicId,
            Role = actor.Staff.Role,
        };
        return BuildService(currentUser, currentStaff);
    }

    public ClinicDashboardService CreatePlatformService(ApplicationUser platformUser)
    {
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = platformUser.Id,
            Email = platformUser.Email,
            Roles = [AppRoles.PlatformAdmin],
        };
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        return BuildService(currentUser, currentStaff);
    }

    public ClinicDashboardService BuildService(FakeCurrentUser currentUser, FakeCurrentStaff currentStaff)
    {
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            audit);

        return new ClinicDashboardService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            audit,
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Clock,
            NullLogger<ClinicDashboardService>.Instance);
    }

    public ClinicReportsService CreateReportService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
    {
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = actor.User.Id,
            Email = actor.User.Email,
            Roles = [actor.Staff.Role],
            OrganizationId = actor.Staff.OrganizationId,
            ClinicId = actor.Staff.ClinicId,
            StaffMemberId = actor.Staff.Id,
        };
        var currentStaff = new FakeCurrentStaff
        {
            HasActiveMembership = true,
            StaffMemberId = actor.Staff.Id,
            OrganizationId = actor.Staff.OrganizationId,
            ClinicId = actor.Staff.ClinicId,
            Role = actor.Staff.Role,
        };
        return BuildReportService(currentUser, currentStaff);
    }

    public ClinicReportsService CreatePlatformReportService(ApplicationUser platformUser)
    {
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = platformUser.Id,
            Email = platformUser.Email,
            Roles = [AppRoles.PlatformAdmin],
        };
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        return BuildReportService(currentUser, currentStaff);
    }

    public ClinicReportsService BuildReportService(FakeCurrentUser currentUser, FakeCurrentStaff currentStaff)
    {
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            audit);

        return new ClinicReportsService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            audit,
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Clock,
            NullLogger<ClinicReportsService>.Instance);
    }

    public async Task SeedAppointmentOnLocalDateAsync(
        Guid clinicId,
        AppointmentStatus status,
        DateOnly localDate,
        Guid? doctorStaffMemberId = null)
    {
        Domain.Staff.StaffMember doctor;
        if (doctorStaffMemberId is Guid id)
        {
            doctor = await Db.StaffMembers.SingleAsync(s => s.Id == id);
        }
        else
        {
            var existing = await Db.StaffMembers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ClinicId == clinicId && s.Role == AppRoles.Doctor);
            if (existing is null)
            {
                var seeded = await SeedStaffAsync(AppRoles.Doctor, clinicId, $"auto-doc-{Guid.NewGuid():N}@test.local");
                doctor = seeded.Staff;
            }
            else
            {
                doctor = existing;
            }
        }

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FirstName = "Appt",
            LastName = "Patient",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicPatient = new ClinicPatient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            PatientId = patient.Id,
            LocalPatientNumber = "P-" + Guid.NewGuid().ToString("N")[..8],
            Status = ClinicPatientStatus.Active,
            Version = 0,
            RegisteredAtUtc = Clock.GetUtcNow(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var startUtc = converter.ToUtc(localDate, new TimeOnly(10, 0), "Asia/Riyadh");

        Db.Patients.Add(patient);
        Db.ClinicPatients.Add(clinicPatient);
        Db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = Org.Id,
            ClinicId = clinicId,
            PatientId = patient.Id,
            ClinicPatientId = clinicPatient.Id,
            DoctorStaffMemberId = doctor.Id,
            AppointmentDateUtc = startUtc,
            DurationMinutes = 30,
            Status = status,
            Source = AppointmentSource.Staff,
            CreatedByUserId = doctor.UserId,
            Version = 0,
            CreatedAtUtc = Clock.GetUtcNow(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    public async Task SeedReminderForClinicAsync(
        Guid clinicId,
        AppointmentReminderStatus status,
        DateTimeOffset scheduledAtUtc)
    {
        await SeedAppointmentAsync(clinicId, AppointmentStatus.Confirmed);
        var appointment = await Db.Appointments
            .Where(a => a.ClinicId == clinicId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstAsync();

        Db.AppointmentReminders.Add(new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            AppointmentId = appointment.Id,
            ReminderType = AppointmentReminderType.Upcoming,
            ScheduledAtUtc = scheduledAtUtc,
            Status = status,
            AttemptCount = status == AppointmentReminderStatus.Failed ? 1 : 0,
            IdempotencyKey = AppointmentReminder.BuildIdempotencyKey(appointment.Id, AppointmentReminderType.Upcoming)
                + ":" + Guid.NewGuid().ToString("N")[..6],
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    public async Task SeedSummaryRunAsync(
        Guid clinicId,
        ClinicAppointmentSummaryRunStatus status,
        DateOnly summaryDate)
    {
        Db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            OrganizationId = Org.Id,
            SummaryDate = summaryDate,
            ScheduledAtUtc = Clock.GetUtcNow(),
            Status = status,
            AttemptCount = status == ClinicAppointmentSummaryRunStatus.Failed ? 1 : 0,
            IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicId, summaryDate)
                + ":" + Guid.NewGuid().ToString("N")[..6],
            AppointmentCount = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}
