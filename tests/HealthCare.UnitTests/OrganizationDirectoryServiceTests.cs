using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class OrganizationDirectoryServiceTests
{
    [Fact]
    public async Task Platform_Admin_Can_Search_Organizations()
    {
        await using var h = await OrgHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("p@test.local");
        var sut = h.CreatePlatformService(platform);

        var page = await sut.SearchAsync(new OrganizationSearchRequest { IsActive = true });
        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(o => o.Name != null && o.Slug != null && o.OrganizationId != Guid.Empty && o.IsActive);
        page.Items.SelectMany(o => typeof(OrganizationDirectoryItemResponse).GetProperties().Select(p => p.Name))
            .Should().NotContain(n => n.Contains("Billing", StringComparison.OrdinalIgnoreCase)
                                      || n.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                                      || n.Contains("Connection", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_By_Name_And_Slug()
    {
        await using var h = await OrgHarness.CreateAsync();
        var sut = h.CreatePlatformService(await h.SeedPlatformAdminAsync("p2@test.local"));

        var byName = await sut.SearchAsync(new OrganizationSearchRequest { Search = "Alpha" });
        byName.Items.Should().ContainSingle(o => o.OrganizationId == h.OrgA.Id);

        var bySlug = await sut.SearchAsync(new OrganizationSearchRequest { Search = "org-b" });
        bySlug.Items.Should().ContainSingle(o => o.OrganizationId == h.OrgB.Id);
    }

    [Fact]
    public async Task Active_Filter_Works()
    {
        await using var h = await OrgHarness.CreateAsync();
        var sut = h.CreatePlatformService(await h.SeedPlatformAdminAsync("p3@test.local"));

        var active = await sut.SearchAsync(new OrganizationSearchRequest { IsActive = true });
        active.Items.Should().OnlyContain(o => o.IsActive);

        var inactive = await sut.SearchAsync(new OrganizationSearchRequest { IsActive = false });
        inactive.Items.Should().ContainSingle(o => o.OrganizationId == h.OrgInactive.Id);
        inactive.Items.Should().OnlyContain(o => !o.IsActive);
    }

    [Fact]
    public async Task Pagination_And_Stable_Sort()
    {
        await using var h = await OrgHarness.CreateAsync();
        var sut = h.CreatePlatformService(await h.SeedPlatformAdminAsync("p4@test.local"));

        var page1 = await sut.SearchAsync(new OrganizationSearchRequest
        {
            Page = 1,
            PageSize = 1,
            SortBy = "name",
            SortDirection = "asc",
        });
        page1.Items.Should().HaveCount(1);
        page1.TotalCount.Should().Be(3);

        var page2 = await sut.SearchAsync(new OrganizationSearchRequest
        {
            Page = 2,
            PageSize = 1,
            SortBy = "name",
            SortDirection = "asc",
        });
        page2.Items.Should().HaveCount(1);
        page1.Items[0].OrganizationId.Should().NotBe(page2.Items[0].OrganizationId);
    }

    [Fact]
    public async Task Invalid_Sort_Rejected_By_Validator()
    {
        var validator = new OrganizationSearchRequestValidator();
        var result = await validator.ValidateAsync(new OrganizationSearchRequest { SortBy = "hack" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Detail_Returns_Safe_Fields_And_Missing_Is_NotFound()
    {
        await using var h = await OrgHarness.CreateAsync();
        var sut = h.CreatePlatformService(await h.SeedPlatformAdminAsync("p5@test.local"));

        var detail = await sut.GetByIdAsync(h.OrgA.Id);
        detail.OrganizationId.Should().Be(h.OrgA.Id);
        detail.Name.Should().Be(h.OrgA.Name);
        detail.Slug.Should().Be(h.OrgA.Slug);
        detail.ClinicCount.Should().Be(2);
        detail.IsActive.Should().BeTrue();

        var act = () => sut.GetByIdAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<OrganizationDirectoryException>()
            .Where(e => e.ErrorCode == OrganizationErrorCodes.NotFound);
    }

    [Fact]
    public async Task Clinic_Admin_Denied()
    {
        await using var h = await OrgHarness.CreateAsync();
        var admin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca@test.local");
        var sut = h.CreateService(admin);

        var act = () => sut.SearchAsync(new OrganizationSearchRequest());
        await act.Should().ThrowAsync<OrganizationDirectoryException>()
            .Where(e => e.ErrorCode == OrganizationErrorCodes.DirectoryAccessDenied);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Browse_All()
    {
        await using var h = await OrgHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.SearchAsync(new OrganizationSearchRequest());
        await act.Should().ThrowAsync<OrganizationDirectoryException>()
            .Where(e => e.ErrorCode == OrganizationErrorCodes.DirectoryAccessDenied);
    }

    [Fact]
    public async Task Patient_Denied()
    {
        await using var h = await OrgHarness.CreateAsync();
        var patientUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "patient@test.local",
            UserName = "patient@test.local",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        (await h.Users.CreateAsync(patientUser, "TempPass_Staff_99!")).Succeeded.Should().BeTrue();
        (await h.Users.AddToRoleAsync(patientUser, AppRoles.Patient)).Succeeded.Should().BeTrue();

        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = patientUser.Id,
            Email = patientUser.Email,
            Roles = [AppRoles.Patient],
        };
        var sut = h.BuildService(currentUser, new FakeCurrentStaff { HasActiveMembership = false });

        var act = () => sut.SearchAsync(new OrganizationSearchRequest());
        await act.Should().ThrowAsync<OrganizationDirectoryException>()
            .Where(e => e.ErrorCode == OrganizationErrorCodes.DirectoryAccessDenied);
    }

    [Fact]
    public void Platform_Admin_Has_Organization_Permissions_But_No_Medical_Notes()
    {
        var perms = RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin);
        perms.Should().Contain(Permissions.Organizations.Read);
        perms.Should().Contain(Permissions.Organizations.Select);
        perms.Should().NotContain(Permissions.MedicalNotes.Read);
        perms.Should().NotContain(Permissions.MedicalNotes.Create);
    }
}

internal sealed class OrgHarness : IAsyncDisposable
{
    public required HealthCareDbContext Db { get; init; }
    public required UserManager<ApplicationUser> Users { get; init; }
    public required Organization OrgA { get; init; }
    public required Organization OrgB { get; init; }
    public required Organization OrgInactive { get; init; }
    public required Domain.Clinics.Clinic ClinicA { get; init; }
    private ServiceProvider? _provider;

    public static async Task<OrgHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>();
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<HealthCareDbContext>();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() });
            }
        }

        var orgA = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Alpha Health",
            Slug = "org-a",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var orgB = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Beta Health",
            Slug = "org-b",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var orgInactive = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Closed Org",
            Slug = "org-closed",
            Status = OrganizationStatus.Inactive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgA.Id,
            Name = "Alpha Clinic",
            Slug = "clinic-a",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA2 = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgA.Id,
            Name = "Alpha Clinic 2",
            Slug = "clinic-a2",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Organizations.AddRange(orgA, orgB, orgInactive);
        db.Clinics.AddRange(clinicA, clinicA2);
        await db.SaveChangesAsync();

        return new OrgHarness
        {
            Db = db,
            Users = users,
            OrgA = orgA,
            OrgB = orgB,
            OrgInactive = orgInactive,
            ClinicA = clinicA,
            _provider = provider,
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
            OrganizationId = OrgA.Id,
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

    public OrganizationDirectoryService CreateService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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

    public OrganizationDirectoryService CreatePlatformService(ApplicationUser platformUser)
    {
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = platformUser.Id,
            Email = platformUser.Email,
            Roles = [AppRoles.PlatformAdmin],
        };
        return BuildService(currentUser, new FakeCurrentStaff { HasActiveMembership = false });
    }

    public OrganizationDirectoryService BuildService(FakeCurrentUser currentUser, FakeCurrentStaff currentStaff)
    {
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            audit);

        return new OrganizationDirectoryService(
            Db,
            currentUser,
            permissions,
            audit,
            NullLogger<OrganizationDirectoryService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}
