using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Security;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Security;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Persistence;
using HealthCare.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class OrganizationSecurityServiceTests
{
    [Fact]
    public async Task Organization_Admin_Lists_Sessions_Without_Token_Secrets()
    {
        await using var h = await SecurityHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-sec@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-sec@test.local");
        await h.SeedRefreshTokenAsync(doctor.User.Id, revoked: false);

        var sut = h.CreateService(orgAdmin);
        var result = await sut.ListSessionsAsync(new OrganizationSecurityQuery());

        result.ActiveSessionCount.Should().BeGreaterThanOrEqualTo(1);
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(i => i.UserId != Guid.Empty && i.SessionId != Guid.Empty);
        typeof(OrganizationSecuritySessionItem).GetProperty("TokenHash").Should().BeNull();
        typeof(OrganizationSecuritySessionItem).GetProperty("RefreshToken").Should().BeNull();
    }

    [Fact]
    public async Task Organization_Admin_Can_Revoke_And_Compromise_Sibling_Clinic_Staff()
    {
        await using var h = await SecurityHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-rev@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-rev@test.local");
        await h.SeedRefreshTokenAsync(doctor.User.Id, revoked: false);

        var sut = h.CreateService(orgAdmin);
        var revoked = await sut.RevokeStaffSessionsAsync(
            doctor.Staff.Id,
            new RevokeOrganizationSessionsRequest { Reason = "test" });
        revoked.RevokedRefreshTokenCount.Should().Be(1);

        var active = await h.Db.RefreshTokens.CountAsync(t => t.UserId == doctor.User.Id && t.RevokedAtUtc == null);
        active.Should().Be(0);

        await h.SeedRefreshTokenAsync(doctor.User.Id, revoked: false);
        var compromised = await sut.RespondToCompromisedAccountAsync(
            doctor.Staff.Id,
            new CompromisedAccountResponseRequest { ExpectedVersion = doctor.Staff.Version, Reason = "suspected" });
        compromised.RevokedRefreshTokenCount.Should().BeGreaterThanOrEqualTo(1);

        var staff = await h.Db.StaffMembers.SingleAsync(s => s.Id == doctor.Staff.Id);
        staff.IsActive.Should().BeFalse();
        var user = await h.Users.FindByIdAsync(doctor.User.Id.ToString());
        user!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_Login_And_Denial_Summaries_Are_Organization_Scoped()
    {
        await using var h = await SecurityHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-sum@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-sum@test.local");

        doctor.User.AccessFailedCount = 3;
        await h.Users.UpdateAsync(doctor.User);

        h.Db.SecurityEvents.AddRange(
            new SecurityEvent
            {
                Id = Guid.NewGuid(),
                EventType = SecurityEventType.FailedLogin,
                Operation = "auth_login",
                ReasonCode = AuthErrorCodes.InvalidCredentials,
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicA.Id,
                TargetUserId = doctor.User.Id,
                OccurredAtUtc = h.Clock.GetUtcNow(),
            },
            new SecurityEvent
            {
                Id = Guid.NewGuid(),
                EventType = SecurityEventType.PermissionDenied,
                Operation = "require_permission",
                ReasonCode = AuthorizationErrorCodes.PermissionDenied,
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicA.Id,
                ActorUserId = doctor.User.Id,
                OccurredAtUtc = h.Clock.GetUtcNow(),
            },
            new SecurityEvent
            {
                Id = Guid.NewGuid(),
                EventType = SecurityEventType.CrossTenantDenied,
                Operation = "appointment_cross_clinic_denied",
                ReasonCode = AuthorizationErrorCodes.ClinicAccessDenied,
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicB.Id,
                ActorUserId = doctor.User.Id,
                OccurredAtUtc = h.Clock.GetUtcNow(),
            });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateService(orgAdmin);
        var failed = await sut.GetFailedLoginSummaryAsync(new OrganizationSecurityQuery());
        failed.UsersWithFailedAttempts.Should().BeGreaterThanOrEqualTo(1);
        failed.FailedLoginEventsInRange.Should().BeGreaterThanOrEqualTo(1);

        var denials = await sut.GetAuthorizationDenialSummaryAsync(new OrganizationSecurityQuery());
        denials.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        denials.EventCategory.Should().Be("authorization_denials");

        var cross = await sut.GetCrossClinicAttemptSummaryAsync(new OrganizationSecurityQuery());
        cross.TotalCount.Should().BeGreaterThanOrEqualTo(1);
        cross.EventCategory.Should().Be("cross_clinic_attempts");
    }

    [Fact]
    public async Task Clinic_Admin_Is_Denied_Organization_Security_Apis()
    {
        await using var h = await SecurityHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-sec@test.local");
        var sut = h.CreateService(clinicAdmin);
        var act = () => sut.ListSessionsAsync(new OrganizationSecurityQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public void Organization_Admin_Has_Security_Session_Read()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().Contain(Permissions.SecuritySessions.Read);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().NotContain(Permissions.SecuritySessions.Read);
        Permissions.All.Should().Contain(Permissions.SecuritySessions.Read);
    }
}

internal sealed class SecurityHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;

    public HealthCareDbContext Db { get; private set; } = null!;

    public UserManager<ApplicationUser> Users { get; private set; } = null!;

    public Organization Org { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicA { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicB { get; private set; } = null!;

    public TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));

    public static async Task<SecurityHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>();
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase("org-sec-" + Guid.NewGuid().ToString("N")));

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
            Name = "Sec Org",
            Slug = "sec-org",
            Status = OrganizationStatus.Active,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic A",
            Slug = "sec-a",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        };
        var clinicB = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic B",
            Slug = "sec-b",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        };
        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new SecurityHarness
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
        };
        Db.StaffMembers.Add(staff);
        await Db.SaveChangesAsync();
        return (user, staff);
    }

    public async Task SeedRefreshTokenAsync(Guid userId, bool revoked)
    {
        Db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = Clock.GetUtcNow().AddHours(-1),
            ExpiresAtUtc = Clock.GetUtcNow().AddDays(7),
            RevokedAtUtc = revoked ? Clock.GetUtcNow() : null,
            CreatedByIp = "127.0.0.1",
            CreatedByUserAgent = "unit-test-agent",
        });
        await Db.SaveChangesAsync();
    }

    public OrganizationSecurityService CreateService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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
        var sessions = new SecuritySessionInvalidationService(
            Db, Users, NullLogger<SecuritySessionInvalidationService>.Instance);
        var events = new RecordingSecurityEvents();

        return new OrganizationSecurityService(
            Db,
            Users,
            currentUser,
            currentStaff,
            permissions,
            audit,
            sessions,
            events,
            Clock,
            NullLogger<OrganizationSecurityService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }

    private sealed class RecordingSecurityEvents : ISecurityEventRecorder
    {
        public void TryRecord(SecurityEventWrite write)
        {
        }
    }
}
