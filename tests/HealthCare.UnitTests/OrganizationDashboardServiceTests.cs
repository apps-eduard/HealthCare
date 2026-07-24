using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class OrganizationDashboardServiceTests
{
    [Fact]
    public async Task Organization_Admin_Sees_Own_Organization_Aggregates()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-dash@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-a@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-b@test.local");
        await h.SeedPatientEnrollmentAsync(h.ClinicA.Id, active: true);
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);
        await h.SeedAppointmentAsync(h.ClinicB.Id, AppointmentStatus.Requested);

        var sut = h.CreateService(orgAdmin);
        var result = await sut.GetAsync(new OrganizationDashboardQuery());

        result.Organization.OrganizationId.Should().Be(h.Org.Id);
        result.Organization.TotalClinicCount.Should().Be(2);
        result.Staff.DoctorCount.Should().Be(2);
        result.Patients.TotalPatientCount.Should().BeGreaterThanOrEqualTo(1);
        result.Appointments.TotalAppointments.Should().Be(2);
        result.Appointments.ConfirmedCount.Should().Be(1);
        result.Appointments.RequestedCount.Should().Be(1);
        result.Context.TimeZoneStrategy.Should().Be("per_clinic_local");
        result.Context.SelectedClinicId.Should().BeNull();
    }

    [Fact]
    public async Task Organization_Admin_Clinic_Filter_Scopes_Counts()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-filter@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-filter-a@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-filter-b@test.local");
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Completed);
        await h.SeedAppointmentAsync(h.ClinicB.Id, AppointmentStatus.Completed);

        var sut = h.CreateService(orgAdmin);
        var result = await sut.GetAsync(new OrganizationDashboardQuery { ClinicId = h.ClinicA.Id });

        result.Organization.TotalClinicCount.Should().Be(1);
        result.Staff.DoctorCount.Should().Be(1);
        result.Appointments.CompletedCount.Should().Be(1);
        result.Context.SelectedClinicId.Should().Be(h.ClinicA.Id);
        result.Context.TimeZoneStrategy.Should().Be("clinic");
        result.Context.TimeZoneId.Should().Be(h.ClinicA.TimeZoneId);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Override_Organization_Id()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-scope@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.GetAsync(new OrganizationDashboardQuery { OrganizationId = Guid.NewGuid() });
        await act.Should().ThrowAsync<OrganizationDashboardException>()
            .Where(e => e.ErrorCode == OrganizationDashboardErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Organization_Admin_Out_Of_Org_Clinic_Is_Not_Found()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-clinic@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.GetAsync(new OrganizationDashboardQuery { ClinicId = Guid.NewGuid() });
        await act.Should().ThrowAsync<OrganizationDashboardException>()
            .Where(e => e.ErrorCode == OrganizationDashboardErrorCodes.ClinicNotFound);
    }

    [Fact]
    public async Task Clinic_Admin_Is_Denied()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-dash@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.GetAsync(new OrganizationDashboardQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_Organization()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("plat-dash@test.local");
        var sut = h.CreatePlatformService(platform);

        var withoutBypass = () => sut.GetAsync(new OrganizationDashboardQuery { OrganizationId = h.Org.Id });
        await withoutBypass.Should().ThrowAsync<AuthorizationException>();

        var withoutOrg = () => sut.GetAsync(new OrganizationDashboardQuery(), PlatformAdminBypass.Explicit);
        await withoutOrg.Should().ThrowAsync<OrganizationDashboardException>()
            .Where(e => e.ErrorCode == OrganizationDashboardErrorCodes.OrganizationScopeRequired);

        var ok = await sut.GetAsync(
            new OrganizationDashboardQuery { OrganizationId = h.Org.Id },
            PlatformAdminBypass.Explicit);
        ok.Organization.OrganizationId.Should().Be(h.Org.Id);
    }

    [Fact]
    public void Organization_Admin_Has_Dashboard_Permission_Clinic_Admin_Does_Not()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().Contain(Permissions.Organizations.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Organizations.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().NotContain(Permissions.Organizations.DashboardRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor)
            .Should().NotContain(Permissions.Organizations.DashboardRead);
        Permissions.All.Should().Contain(Permissions.Organizations.DashboardRead);
    }
}

internal sealed class DashboardHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;

    public HealthCareDbContext Db { get; private set; } = null!;

    public UserManager<ApplicationUser> Users { get; private set; } = null!;

    public Organization Org { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicA { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicB { get; private set; } = null!;

    public TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));

    public static async Task<DashboardHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>();
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase("org-dash-" + Guid.NewGuid().ToString("N")));

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
            Name = "Dash Org",
            Slug = "dash-org",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic A",
            Slug = "clinic-a",
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
            Slug = "clinic-b",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new DashboardHarness
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
            RegisteredAtUtc = DateTimeOffset.UtcNow,
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
            RegisteredAtUtc = DateTimeOffset.UtcNow,
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
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();
    }

    public OrganizationDashboardService CreateService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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

    public OrganizationDashboardService CreatePlatformService(ApplicationUser platformUser)
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

    public OrganizationDashboardService BuildService(FakeCurrentUser currentUser, FakeCurrentStaff currentStaff)
    {
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            audit);

        return new OrganizationDashboardService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            audit,
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Clock,
            NullLogger<OrganizationDashboardService>.Instance);
    }

    public OrganizationReportService CreateReportService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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

    public OrganizationReportService CreatePlatformReportService(ApplicationUser platformUser)
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

    public OrganizationReportService BuildReportService(FakeCurrentUser currentUser, FakeCurrentStaff currentStaff)
    {
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            audit);

        return new OrganizationReportService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            audit,
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Clock,
            NullLogger<OrganizationReportService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}
