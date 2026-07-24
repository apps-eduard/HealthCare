using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Application.Identity;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HealthCare.UnitTests;

public sealed class OrganizationAuditAndUsageServiceTests
{
    [Fact]
    public async Task Organization_Admin_Lists_Audit_Logs_In_Own_Organization_Only()
    {
        await using var h = await AuditUsageHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-audit@test.local");
        var foreign = await h.SeedForeignOrgAsync();

        h.Db.OrganizationAuditEvents.AddRange(
            new OrganizationAuditEvent
            {
                Id = Guid.NewGuid(),
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicA.Id,
                ActorUserId = orgAdmin.User.Id,
                Category = "clinic",
                Action = "clinic_created",
                ResultCode = "succeeded",
                CorrelationId = "corr-own",
                OccurredAtUtc = h.Clock.GetUtcNow().AddMinutes(-5),
            },
            new OrganizationAuditEvent
            {
                Id = Guid.NewGuid(),
                OrganizationId = foreign.Org.Id,
                ClinicId = foreign.Clinic.Id,
                ActorUserId = Guid.NewGuid(),
                Category = "clinic",
                Action = "clinic_created",
                ResultCode = "succeeded",
                CorrelationId = "corr-foreign",
                OccurredAtUtc = h.Clock.GetUtcNow().AddMinutes(-4),
            });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAuditService(orgAdmin);
        var page = await sut.SearchAsync(new OrganizationAuditLogQuery());

        page.Items.Should().HaveCount(1);
        page.Items[0].Action.Should().Be("clinic_created");
        page.Items[0].CorrelationId.Should().Be("corr-own");
        page.OrganizationId.Should().Be(h.Org.Id);
        page.RetentionDays.Should().Be(365);
        typeof(OrganizationAuditLogItem).GetProperty("Password").Should().BeNull();
        typeof(OrganizationAuditLogItem).GetProperty("Token").Should().BeNull();
    }

    [Fact]
    public async Task Correlation_Lookup_Returns_Matching_Events()
    {
        await using var h = await AuditUsageHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-corr@test.local");
        var eventId = Guid.NewGuid();
        h.Db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = h.Org.Id,
            ClinicId = h.ClinicA.Id,
            ActorUserId = orgAdmin.User.Id,
            Category = "staff",
            Action = "staff_created",
            ResultCode = "succeeded",
            CorrelationId = "corr-lookup",
            OccurredAtUtc = h.Clock.GetUtcNow(),
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAuditService(orgAdmin);
        var byCorr = await sut.GetByCorrelationIdAsync("corr-lookup", new OrganizationAuditLogQuery());
        byCorr.Items.Should().ContainSingle(i => i.Id == eventId);

        var detail = await sut.GetByIdAsync(eventId, new OrganizationAuditLogQuery());
        detail.Event.Id.Should().Be(eventId);
        detail.Event.Action.Should().Be("staff_created");
    }

    [Fact]
    public async Task Clinic_Admin_Cannot_Read_Audit_Logs()
    {
        await using var h = await AuditUsageHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit@test.local");
        var sut = h.CreateAuditService(clinicAdmin);

        var act = () => sut.SearchAsync(new OrganizationAuditLogQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Usage_Shows_Counts_And_Remaining_Capacity()
    {
        await using var h = await AuditUsageHarness.CreateAsync();
        h.Org.MaxClinics = 5;
        h.Org.MaxStaff = 10;
        await h.Db.SaveChangesAsync();

        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-usage@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-usage@test.local");

        var sut = h.CreateUsageService(orgAdmin);
        var usage = await sut.GetUsageAsync(new OrganizationUsageQuery());

        usage.ClinicCount.Should().Be(2);
        usage.MaxClinics.Should().Be(5);
        usage.RemainingClinicCapacity.Should().Be(3);
        usage.StaffCount.Should().Be(2);
        usage.ActiveDoctorCount.Should().Be(1);
        usage.MaxStaff.Should().Be(10);
        usage.RemainingStaffCapacity.Should().Be(8);
        usage.ClinicLimitReached.Should().BeFalse();
        usage.AuditRetentionDays.Should().Be(365);
    }

    [Fact]
    public async Task Clinic_Create_Is_Blocked_When_Limit_Reached()
    {
        await using var h = await AuditUsageHarness.CreateAsync();
        h.Org.MaxClinics = 2;
        await h.Db.SaveChangesAsync();

        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-limit@test.local");
        var clinicSvc = h.CreateClinicService(orgAdmin);

        var act = () => clinicSvc.CreateAsync(new CreateOrganizationClinicRequest
        {
            Name = "Overflow",
            Slug = "overflow-clinic",
            TimeZoneId = "Asia/Riyadh",
        });
        await act.Should().ThrowAsync<ClinicManagementException>()
            .Where(e => e.ErrorCode == ClinicManagementErrorCodes.LimitReached);
    }

    [Fact]
    public void Permissions_Include_Audit_And_Usage_For_Org_Admin()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().Contain(Permissions.Organizations.AuditLogsRead)
            .And.Contain(Permissions.Organizations.UsageRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().NotContain(Permissions.Organizations.AuditLogsRead)
            .And.NotContain(Permissions.Organizations.UsageRead);
        Permissions.All.Should().Contain(Permissions.Organizations.AuditLogsRead)
            .And.Contain(Permissions.Organizations.UsageRead);
    }
}

sealed class AuditUsageHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;
    public required HealthCareDbContext Db { get; init; }
    public required UserManager<ApplicationUser> Users { get; init; }
    public required Organization Org { get; init; }
    public required Domain.Clinics.Clinic ClinicA { get; init; }
    public required Domain.Clinics.Clinic ClinicB { get; init; }
    public TimeProvider Clock { get; } = TimeProvider.System;

    public static async Task<AuditUsageHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase("audit-usage-" + Guid.NewGuid()));
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<HealthCareDbContext>();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Audit Org",
            Slug = "audit-org",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic A",
            Slug = "audit-a",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        };
        var clinicB = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic B",
            Slug = "audit-b",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        };
        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new AuditUsageHarness
        {
            _provider = provider,
            Db = db,
            Users = users,
            Org = org,
            ClinicA = clinicA,
            ClinicB = clinicB,
        };
    }

    public async Task<(Organization Org, Domain.Clinics.Clinic Clinic)> SeedForeignOrgAsync()
    {
        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Foreign Org",
            Slug = "foreign-audit",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinic = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Foreign Clinic",
            Slug = "foreign-audit-c",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        };
        Db.Organizations.Add(org);
        Db.Clinics.Add(clinic);
        await Db.SaveChangesAsync();
        return (org, clinic);
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

    public OrganizationAuditLogService CreateAuditService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
    {
        var (currentUser, currentStaff, permissions, audit) = BuildActor(actor);
        return new OrganizationAuditLogService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            audit,
            Options.Create(new AuditRetentionOptions { RetentionDays = 365 }));
    }

    public OrganizationUsageService CreateUsageService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
    {
        var (currentUser, currentStaff, permissions, audit) = BuildActor(actor);
        var limits = new OrganizationLimitService(
            Db,
            Options.Create(new OrganizationLimitsOptions()),
            Clock);
        return new OrganizationUsageService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            audit,
            limits,
            Options.Create(new AuditRetentionOptions { RetentionDays = 365 }),
            Clock);
    }

    public ClinicManagementService CreateClinicService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
    {
        var (currentUser, currentStaff, permissions, audit) = BuildActor(actor);
        var roleAssignment = new RoleAssignmentAuthorizationService(audit);
        return new ClinicManagementService(
            Db,
            Users,
            currentUser,
            currentStaff,
            permissions,
            roleAssignment,
            audit,
            new OrganizationLimitService(Db, Options.Create(new OrganizationLimitsOptions()), Clock),
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Clock,
            NullLogger<ClinicManagementService>.Instance);
    }

    private static (FakeCurrentUser, FakeCurrentStaff, PermissionService, NoOpAuthorizationAuditLogger) BuildActor(
        (ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(currentUser, currentStaff, new FakeCurrentPatient(), audit);
        return (currentUser, currentStaff, permissions, audit);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}
