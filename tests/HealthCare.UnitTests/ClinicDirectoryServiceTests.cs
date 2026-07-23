using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Clinics;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class ClinicDirectoryServiceTests
{
    [Fact]
    public async Task Clinic_Admin_Receives_Only_Own_Clinic()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var admin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca@test.local");
        var sut = h.CreateService(admin);

        var page = await sut.SearchAsync(new ClinicSearchRequest());
        page.Items.Should().HaveCount(1);
        page.Items[0].ClinicId.Should().Be(h.ClinicA.Id);
    }

    [Fact]
    public async Task Clinic_Admin_Out_Of_Scope_Detail_Returns_Not_Found()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var admin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca2@test.local");
        var sut = h.CreateService(admin);

        var act = () => sut.GetByIdAsync(h.ClinicB.Id);
        await act.Should().ThrowAsync<ClinicDirectoryException>()
            .Where(e => e.ErrorCode == ClinicErrorCodes.NotFound);
    }

    [Fact]
    public async Task Organization_Admin_Lists_Own_Organization_Clinics()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa@test.local");
        var sut = h.CreateService(orgAdmin);

        var page = await sut.SearchAsync(new ClinicSearchRequest());
        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(c => c.OrganizationId == h.Org.Id);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Override_Organization_Id()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa2@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.SearchAsync(new ClinicSearchRequest { OrganizationId = Guid.NewGuid() });
        await act.Should().ThrowAsync<ClinicDirectoryException>()
            .Where(e => e.ErrorCode == ClinicErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Platform_Admin_Denied_Without_Bypass()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("p@test.local");
        var sut = h.CreatePlatformService(platform);

        var act = () => sut.SearchAsync(new ClinicSearchRequest { OrganizationId = h.Org.Id });
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Organization_Scope_With_Bypass()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("p2@test.local");
        var sut = h.CreatePlatformService(platform);

        var act = () => sut.SearchAsync(new ClinicSearchRequest(), PlatformAdminBypass.Explicit);
        await act.Should().ThrowAsync<ClinicDirectoryException>()
            .Where(e => e.ErrorCode == ClinicErrorCodes.OrganizationScopeRequired);
    }

    [Fact]
    public async Task Platform_Admin_Bypass_Lists_Organization_Clinics()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("p3@test.local");
        var sut = h.CreatePlatformService(platform);

        var page = await sut.SearchAsync(
            new ClinicSearchRequest { OrganizationId = h.Org.Id },
            PlatformAdminBypass.Explicit);

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(c => c.OrganizationId == h.Org.Id);
    }

    [Fact]
    public async Task Search_By_Name_And_Slug()
    {
        await using var h = await ClinicHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa3@test.local");
        var sut = h.CreateService(orgAdmin);

        var byName = await sut.SearchAsync(new ClinicSearchRequest { Search = "Alpha" });
        byName.Items.Should().ContainSingle(c => c.ClinicId == h.ClinicA.Id);

        var bySlug = await sut.SearchAsync(new ClinicSearchRequest { Search = "clinic-b" });
        bySlug.Items.Should().ContainSingle(c => c.ClinicId == h.ClinicB.Id);
    }

    [Fact]
    public async Task Invalid_Sort_Rejected_By_Validator()
    {
        var validator = new ClinicSearchRequestValidator();
        var result = await validator.ValidateAsync(new ClinicSearchRequest { SortBy = "hack" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Patient_Denied()
    {
        await using var h = await ClinicHarness.CreateAsync();
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
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        var sut = h.BuildService(currentUser, currentStaff);

        var act = () => sut.SearchAsync(new ClinicSearchRequest());
        await act.Should().ThrowAsync<ClinicDirectoryException>()
            .Where(e => e.ErrorCode == ClinicErrorCodes.DirectoryAccessDenied);
    }
}

internal sealed class ClinicHarness : IAsyncDisposable
{
    public required HealthCareDbContext Db { get; init; }
    public required UserManager<ApplicationUser> Users { get; init; }
    public required Organization Org { get; init; }
    public required Domain.Clinics.Clinic ClinicA { get; init; }
    public required Domain.Clinics.Clinic ClinicB { get; init; }
    private ServiceProvider? _provider;

    public static async Task<ClinicHarness> CreateAsync()
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

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Org",
            Slug = "org",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Alpha Clinic",
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
            Name = "Beta Clinic",
            Slug = "clinic-b",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new ClinicHarness
        {
            Db = db,
            Users = users,
            Org = org,
            ClinicA = clinicA,
            ClinicB = clinicB,
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

    public ClinicDirectoryService CreateService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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

    public ClinicDirectoryService CreatePlatformService(ApplicationUser platformUser)
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

    public ClinicDirectoryService BuildService(FakeCurrentUser currentUser, FakeCurrentStaff currentStaff)
    {
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            audit);

        return new ClinicDirectoryService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            audit,
            NullLogger<ClinicDirectoryService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}
