using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
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
using Microsoft.Extensions.Options;

namespace HealthCare.UnitTests;

public sealed class OrganizationSettingsServiceTests
{
    [Fact]
    public async Task Organization_Admin_Can_Read_And_Update_Profile()
    {
        await using var h = await SettingsHarness.CreateAsync();
        h.Org.ContactEmail = "before@example.com";
        h.Org.MaxClinics = 5;
        h.Org.MaxStaff = 20;
        await h.Db.SaveChangesAsync();

        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-settings@test.local");
        var sut = h.CreateService(orgAdmin);

        var before = await sut.GetAsync(new OrganizationSettingsQuery());
        before.Name.Should().Be("Settings Org");
        before.ContactEmail.Should().Be("before@example.com");
        before.MaxClinics.Should().Be(5);
        before.ClinicCount.Should().Be(2);
        before.RemainingClinicCapacity.Should().Be(3);
        before.Version.Should().Be(0);

        var updated = await sut.UpdateAsync(
            new UpdateOrganizationSettingsRequest
            {
                ExpectedVersion = before.Version,
                Name = "Settings Org Renamed",
                ContactEmail = "ops@example.com",
                ContactPhone = "+966500000000",
                Country = "SA",
                DefaultTimeZoneId = "Asia/Riyadh",
                BrandingPlaceholder = "Acme Health",
            },
            new OrganizationSettingsQuery());

        updated.Name.Should().Be("Settings Org Renamed");
        updated.ContactEmail.Should().Be("ops@example.com");
        updated.ContactPhone.Should().Be("+966500000000");
        updated.Country.Should().Be("SA");
        updated.DefaultTimeZoneId.Should().Be("Asia/Riyadh");
        updated.BrandingPlaceholder.Should().Be("Acme Health");
        updated.Version.Should().Be(1);
        updated.Slug.Should().Be("settings-org");
        updated.Status.Should().Be(nameof(OrganizationStatus.Active));
        updated.MaxClinics.Should().Be(5);

        h.Audit.Operations.Should().Contain(o =>
            o.Operation == "organization_profile_update" && o.ResultCode == "succeeded");
    }

    [Fact]
    public async Task Concurrency_Conflict_Is_Rejected()
    {
        await using var h = await SettingsHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-conc@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.UpdateAsync(
            new UpdateOrganizationSettingsRequest
            {
                ExpectedVersion = 99,
                Name = "Should Fail",
            },
            new OrganizationSettingsQuery());

        await act.Should().ThrowAsync<OrganizationSettingsException>()
            .Where(e => e.ErrorCode == OrganizationSettingsErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Cross_Organization_Override_Is_Denied()
    {
        await using var h = await SettingsHarness.CreateAsync();
        var foreign = await h.SeedForeignOrgAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-cross@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.GetAsync(new OrganizationSettingsQuery { OrganizationId = foreign.Org.Id });
        await act.Should().ThrowAsync<OrganizationSettingsException>()
            .Where(e => e.ErrorCode == OrganizationSettingsErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Clinic_Admin_And_Patient_Are_Denied()
    {
        await using var h = await SettingsHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-settings@test.local");
        var clinicSut = h.CreateService(clinicAdmin);
        await FluentActions.Awaiting(() => clinicSut.GetAsync(new OrganizationSettingsQuery()))
            .Should().ThrowAsync<AuthorizationException>();

        var patient = await h.SeedPatientAsync("patient-settings@test.local");
        var patientSut = h.CreatePatientService(patient);
        await FluentActions.Awaiting(() => patientSut.GetAsync(new OrganizationSettingsQuery()))
            .Should().ThrowAsync<OrganizationSettingsException>()
            .Where(e => e.ErrorCode == OrganizationSettingsErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_Organization()
    {
        await using var h = await SettingsHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("plat-settings@test.local");
        var sut = h.CreatePlatformService(platform);

        await FluentActions.Awaiting(() => sut.GetAsync(new OrganizationSettingsQuery { OrganizationId = h.Org.Id }))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => sut.GetAsync(new OrganizationSettingsQuery(), PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<OrganizationSettingsException>()
            .Where(e => e.ErrorCode == OrganizationSettingsErrorCodes.OrganizationScopeRequired);

        var ok = await sut.GetAsync(
            new OrganizationSettingsQuery { OrganizationId = h.Org.Id },
            PlatformAdminBypass.Explicit);
        ok.OrganizationId.Should().Be(h.Org.Id);
    }

    [Fact]
    public async Task Read_Only_Permission_Cannot_Update()
    {
        await using var h = await SettingsHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-readonly@test.local");
        var sut = h.CreateService(orgAdmin, revokeUpdate: true);

        var act = () => sut.UpdateAsync(
            new UpdateOrganizationSettingsRequest
            {
                ExpectedVersion = 0,
                Name = "Blocked",
            },
            new OrganizationSettingsQuery());

        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public void Permissions_Include_Profile_For_Org_And_Platform_Admin()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().Contain(Permissions.Organizations.ProfileRead)
            .And.Contain(Permissions.Organizations.ProfileUpdate);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Organizations.ProfileRead)
            .And.Contain(Permissions.Organizations.ProfileUpdate);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().NotContain(Permissions.Organizations.ProfileRead);
        Permissions.All.Should().Contain(Permissions.Organizations.ProfileRead)
            .And.Contain(Permissions.Organizations.ProfileUpdate);
    }
}

sealed class SettingsHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;
    public required HealthCareDbContext Db { get; init; }
    public required UserManager<ApplicationUser> Users { get; init; }
    public required Organization Org { get; init; }
    public required Domain.Clinics.Clinic ClinicA { get; init; }
    public required Domain.Clinics.Clinic ClinicB { get; init; }
    public RecordingOrganizationAuditLogger Audit { get; } = new();
    public TimeProvider Clock { get; } = TimeProvider.System;

    public static async Task<SettingsHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase("org-settings-" + Guid.NewGuid()));
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
            Name = "Settings Org",
            Slug = "settings-org",
            Status = OrganizationStatus.Active,
            Version = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic A",
            Slug = "settings-a",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        };
        var clinicB = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic B",
            Slug = "settings-b",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
        };
        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new SettingsHarness
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
            Name = "Foreign Settings Org",
            Slug = "foreign-settings",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinic = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Foreign Clinic",
            Slug = "foreign-settings-c",
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

    public async Task<ApplicationUser> SeedPatientAsync(string email)
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
        (await Users.CreateAsync(user, "TempPass_Patient_99!")).Succeeded.Should().BeTrue();
        (await Users.AddToRoleAsync(user, AppRoles.Patient)).Succeeded.Should().BeTrue();
        return user;
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
        (await Users.CreateAsync(user, "TempPass_Admin_99!")).Succeeded.Should().BeTrue();
        (await Users.AddToRoleAsync(user, AppRoles.PlatformAdmin)).Succeeded.Should().BeTrue();
        return user;
    }

    public OrganizationSettingsService CreateService(
        (ApplicationUser User, Domain.Staff.StaffMember Staff) actor,
        bool revokeUpdate = false)
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
        IPermissionService permissions = revokeUpdate
            ? new RestrictedPermissionService(currentUser, currentStaff, Audit, Permissions.Organizations.ProfileUpdate)
            : new PermissionService(currentUser, currentStaff, new FakeCurrentPatient(), Audit);

        return new OrganizationSettingsService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            Audit,
            new OrganizationLimitService(Db, Options.Create(new OrganizationLimitsOptions()), Clock),
            Clock);
    }

    public OrganizationSettingsService CreatePatientService(ApplicationUser patient)
    {
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = patient.Id,
            Email = patient.Email,
            Roles = [AppRoles.Patient],
        };
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        var permissions = new PermissionService(currentUser, currentStaff, new FakeCurrentPatient(), Audit);
        return new OrganizationSettingsService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            Audit,
            new OrganizationLimitService(Db, Options.Create(new OrganizationLimitsOptions()), Clock),
            Clock);
    }

    public OrganizationSettingsService CreatePlatformService(ApplicationUser platform)
    {
        var currentUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = platform.Id,
            Email = platform.Email,
            Roles = [AppRoles.PlatformAdmin],
        };
        var currentStaff = new FakeCurrentStaff { HasActiveMembership = false };
        var permissions = new PermissionService(currentUser, currentStaff, new FakeCurrentPatient(), Audit);
        return new OrganizationSettingsService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            Audit,
            new OrganizationLimitService(Db, Options.Create(new OrganizationLimitsOptions()), Clock),
            Clock);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}

sealed class RecordingOrganizationAuditLogger : NoOpAuthorizationAuditLogger
{
    public List<(string Operation, string ResultCode, Guid? OrganizationId)> Operations { get; } = [];

    public override void OrganizationOperation(string operation, string resultCode, Guid? organizationId = null)
    {
        Operations.Add((operation, resultCode, organizationId));
    }
}

/// <summary>
/// Permission service that denies a single named permission while preserving the rest of the matrix.
/// </summary>
sealed class RestrictedPermissionService : IPermissionService
{
    private readonly PermissionService _inner;
    private readonly string _denied;

    public RestrictedPermissionService(
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IAuthorizationAuditLogger audit,
        string deniedPermission)
    {
        _inner = new PermissionService(currentUser, currentStaff, new FakeCurrentPatient(), audit);
        _denied = deniedPermission;
    }

    public bool HasPermission(string permission) =>
        !string.Equals(permission, _denied, StringComparison.Ordinal)
        && _inner.HasPermission(permission);

    public bool HasAnyPermission(params string[] permissions) =>
        permissions.Any(HasPermission);

    public void RequirePermission(string permission)
    {
        if (!HasPermission(permission))
        {
            throw AuthorizationException.PermissionDenied(permission);
        }
    }

    public IReadOnlyList<string> GetCurrentPermissions() =>
        _inner.GetCurrentPermissions().Where(p => !string.Equals(p, _denied, StringComparison.Ordinal)).ToArray();

    public IReadOnlyList<string> GetEffectiveRoles() => _inner.GetEffectiveRoles();
}
