using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class ClinicManagementServiceTests
{
    [Fact]
    public async Task Organization_Admin_Can_Create_And_List_Clinic()
    {
        await using var h = await ClinicMgmtHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-clinic@test.local");
        var sut = h.CreateService(orgAdmin);

        var created = await sut.CreateAsync(new CreateOrganizationClinicRequest
        {
            Name = "North Clinic",
            Slug = "north-clinic",
            TimeZoneId = "Asia/Riyadh",
            City = "Riyadh",
        });

        created.Slug.Should().Be("north-clinic");
        created.IsActive.Should().BeTrue();
        created.Version.Should().Be(0);

        var page = await sut.SearchAsync(new OrganizationClinicSearchRequest());
        page.Items.Should().Contain(c => c.ClinicId == created.ClinicId);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Deactivate_Last_Active_Clinic()
    {
        await using var h = await ClinicMgmtHarness.CreateAsync();
        // Only one active clinic for this harness org after deactivating B.
        h.ClinicB.IsActive = false;
        await h.Db.SaveChangesAsync();

        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-last@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.DeactivateAsync(
            h.ClinicA.Id,
            new ClinicActivationRequest { ExpectedVersion = h.ClinicA.Version });
        await act.Should().ThrowAsync<ClinicManagementException>()
            .Where(e => e.ErrorCode == ClinicManagementErrorCodes.DeactivationNotAllowed);
    }

    [Fact]
    public async Task Duplicate_Slug_Is_Conflict()
    {
        await using var h = await ClinicMgmtHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-slug@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.CreateAsync(new CreateOrganizationClinicRequest
        {
            Name = "Clone",
            Slug = h.ClinicA.Slug,
            TimeZoneId = "Asia/Riyadh",
        });
        await act.Should().ThrowAsync<ClinicManagementException>()
            .Where(e => e.ErrorCode == ClinicManagementErrorCodes.SlugInUse);
    }

    [Fact]
    public async Task Clinic_Admin_Is_Denied_Management_Api()
    {
        await using var h = await ClinicMgmtHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-mgmt@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.SearchAsync(new OrganizationClinicSearchRequest());
        await act.Should().ThrowAsync<ClinicManagementException>()
            .Where(e => e.ErrorCode == ClinicManagementErrorCodes.AccessDenied);
    }

    [Fact]
    public void Permissions_Matrix_Grants_Create_To_Org_Admin_Not_Clinic_Admin()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().Contain(Permissions.Clinics.Create)
            .And.Contain(Permissions.Clinics.Update)
            .And.Contain(Permissions.Clinics.Activate)
            .And.Contain(Permissions.Clinics.Deactivate);

        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().Contain(Permissions.Clinics.Read)
            .And.NotContain(Permissions.Clinics.Create)
            .And.NotContain(Permissions.Clinics.Manage);

        ClinicSlugRules.IsValid("downtown-clinic").Should().BeTrue();
        ClinicSlugRules.IsValid("Bad Slug").Should().BeFalse();
    }
}

internal sealed class ClinicMgmtHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;

    public HealthCareDbContext Db { get; private set; } = null!;
    public UserManager<ApplicationUser> Users { get; private set; } = null!;
    public Organization Org { get; private set; } = null!;
    public Domain.Clinics.Clinic ClinicA { get; private set; } = null!;
    public Domain.Clinics.Clinic ClinicB { get; private set; } = null!;
    public TimeProvider Clock { get; } = new FixedTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));

    public static async Task<ClinicMgmtHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>();
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase("clinic-mgmt-" + Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));

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
            Name = "Clinic Org",
            Slug = "clinic-org",
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
            Version = 0,
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
            Version = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new ClinicMgmtHarness
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

    public ClinicManagementService CreateService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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
        var roleAssignment = new RoleAssignmentAuthorizationService(audit);

        return new ClinicManagementService(
            Db,
            Users,
            currentUser,
            currentStaff,
            permissions,
            roleAssignment,
            audit,
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            Clock,
            NullLogger<ClinicManagementService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}
