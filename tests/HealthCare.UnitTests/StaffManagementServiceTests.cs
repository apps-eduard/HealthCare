using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Staff;
using HealthCare.Contracts.Staff;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Authorization;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Persistence;
using HealthCare.Infrastructure.Staff;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class StaffManagementServiceTests
{
    [Fact]
    public async Task Clinic_Admin_Lists_Only_Own_Clinic()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doca@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "docb@test.local");

        var sut = h.CreateService(clinicAdmin);
        var page = await sut.SearchAsync(new StaffSearchRequest());
        page.Items.Should().OnlyContain(i => i.ClinicId == h.ClinicA.Id);
        page.Items.Should().NotContain(i => i.Email == "docb@test.local");
    }

    [Fact]
    public async Task Clinic_Admin_Cannot_Assign_Organization_Admin()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca2@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc2@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.AssignRoleAsync(doctor.Staff.Id, AppRoles.OrganizationAdmin);
        await act.Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.RoleAssignmentDenied);
    }

    [Fact]
    public async Task Create_Receptionist_Succeeds_For_Clinic_Admin()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca3@test.local");
        var sut = h.CreateService(clinicAdmin);

        var created = await sut.CreateAsync(new CreateStaffRequest
        {
            Email = "reception@test.local",
            FirstName = "Rex",
            LastName = "Reception",
            Role = AppRoles.Receptionist,
            TemporaryPassword = "TempPass_Staff_99!",
        });

        created.Staff.Role.Should().Be(AppRoles.Receptionist);
        created.Staff.ClinicId.Should().Be(h.ClinicA.Id);
        (await h.Db.Users.CountAsync(u => u.Email == "reception@test.local")).Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_Email_Does_Not_Leave_Partial_User()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca4@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "dup@test.local");
        var sut = h.CreateService(clinicAdmin);
        var before = await h.Db.Users.CountAsync();

        var act = () => sut.CreateAsync(new CreateStaffRequest
        {
            Email = "dup@test.local",
            FirstName = "Dup",
            LastName = "User",
            Role = AppRoles.Nurse,
            TemporaryPassword = "TempPass_Staff_99!",
        });

        await act.Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.EmailInUse);
        (await h.Db.Users.CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Self_Elevation_Denied()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca5@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.AssignRoleAsync(clinicAdmin.Staff.Id, AppRoles.Doctor);
        await act.Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.SelfElevationDenied);
    }

    [Fact]
    public async Task Deactivation_Revokes_Refresh_Tokens()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca6@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc6@test.local");
        h.Db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = doctor.User.Id,
            TokenHash = "hash",
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateService(clinicAdmin);
        await sut.DeactivateAsync(doctor.Staff.Id, new StaffActivationRequest { ExpectedVersion = doctor.Staff.Version });

        var token = await h.Db.RefreshTokens.SingleAsync(t => t.UserId == doctor.User.Id);
        token.RevokedAtUtc.Should().NotBeNull();
        (await h.Db.StaffMembers.SingleAsync(s => s.Id == doctor.Staff.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Stale_Update_Returns_Concurrency_Conflict()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca7@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc7@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.UpdateAsync(doctor.Staff.Id, new UpdateStaffRequest
        {
            FirstName = "Changed",
            ExpectedVersion = doctor.Staff.Version + 5,
        });

        await act.Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Assign_Platform_Admin()
    {
        await using var h = await StaffHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "docoa@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.AssignRoleAsync(doctor.Staff.Id, AppRoles.PlatformAdmin);
        await act.Should().ThrowAsync<StaffManagementException>();
    }

    [Fact]
    public async Task Platform_Admin_Without_Bypass_Cannot_List()
    {
        await using var h = await StaffHarness.CreateAsync();
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "docp@test.local");
        var platform = await h.SeedPlatformAdminAsync("platform@test.local");
        var sut = h.CreatePlatformService(platform);

        var act = () => sut.SearchAsync(new StaffSearchRequest { ClinicId = h.ClinicA.Id });
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Explicit_Bypass_Lists_Clinic()
    {
        await using var h = await StaffHarness.CreateAsync();
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "docpb@test.local");
        var platform = await h.SeedPlatformAdminAsync("platform2@test.local");
        var sut = h.CreatePlatformService(platform);

        var page = await sut.SearchAsync(
            new StaffSearchRequest { ClinicId = h.ClinicA.Id },
            PlatformAdminBypass.Explicit);

        page.Items.Should().OnlyContain(i => i.ClinicId == h.ClinicA.Id);
        page.Items.Should().Contain(i => i.Email == "docpb@test.local");
    }

    [Fact]
    public void Patient_Role_Rejected_By_Validator_Matrix()
    {
        RolePermissionMatrix.RoleHasPermission(AppRoles.Patient, Permissions.Staff.Manage).Should().BeFalse();
        AppRoles.All.Should().Contain(AppRoles.Patient);
    }

    [Fact]
    public async Task Organization_Admin_Can_Change_Clinic_For_Doctor()
    {
        await using var h = await StaffHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-move@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-move@test.local");
        var sut = h.CreateService(orgAdmin);

        var updated = await sut.ChangeClinicAsync(doctor.Staff.Id, new ChangeStaffClinicRequest
        {
            NewClinicId = h.ClinicB.Id,
            ExpectedVersion = doctor.Staff.Version,
            AdministrativeReason = "Coverage rotation",
        });

        updated.ClinicId.Should().Be(h.ClinicB.Id);
        updated.ClinicName.Should().Be(h.ClinicB.Name);
        (await h.Db.StaffMembers.SingleAsync(s => s.Id == doctor.Staff.Id)).ClinicId.Should().Be(h.ClinicB.Id);
    }

    [Fact]
    public async Task Clinic_Admin_Cannot_Change_Clinic()
    {
        await using var h = await StaffHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-move@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-move2@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.ChangeClinicAsync(doctor.Staff.Id, new ChangeStaffClinicRequest
        {
            NewClinicId = h.ClinicB.Id,
            ExpectedVersion = doctor.Staff.Version,
            AdministrativeReason = "Attempt",
        });
        await act.Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.ClinicChangeNotAllowed);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Change_Clinic_For_Organization_Admin()
    {
        await using var h = await StaffHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa1@test.local");
        var otherAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa2@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.ChangeClinicAsync(otherAdmin.Staff.Id, new ChangeStaffClinicRequest
        {
            NewClinicId = h.ClinicB.Id,
            ExpectedVersion = otherAdmin.Staff.Version,
            AdministrativeReason = "Attempt",
        });
        await act.Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.ClinicChangeNotAllowed);
    }

    [Fact]
    public async Task Self_Deactivation_Uses_Dedicated_Error()
    {
        await using var h = await StaffHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-self@test.local");
        await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-peer@test.local");
        var sut = h.CreateService(orgAdmin);

        var act = () => sut.DeactivateAsync(orgAdmin.Staff.Id, new StaffActivationRequest
        {
            ExpectedVersion = orgAdmin.Staff.Version,
        });
        await act.Should().ThrowAsync<StaffManagementException>()
            .Where(e => e.ErrorCode == StaffErrorCodes.SelfDeactivationDenied);
    }

    [Fact]
    public async Task Revoke_Sessions_Is_Idempotent()
    {
        await using var h = await StaffHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-rev@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-rev@test.local");
        h.Db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = doctor.User.Id,
            TokenHash = "rev-hash",
            FamilyId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        });
        await h.Db.SaveChangesAsync();
        var sut = h.CreateService(orgAdmin);

        var first = await sut.RevokeSessionsAsync(doctor.Staff.Id, new RevokeStaffSessionsRequest());
        var second = await sut.RevokeSessionsAsync(doctor.Staff.Id, new RevokeStaffSessionsRequest());
        first.Message.Should().NotBeNullOrWhiteSpace();
        second.Message.Should().NotBeNullOrWhiteSpace();
        (await h.Db.RefreshTokens.SingleAsync(t => t.UserId == doctor.User.Id)).RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Password_Reset_Captures_Development_Token()
    {
        await using var h = await StaffHarness.CreateAsync();
        var store = new DevelopmentPasswordResetTokenStore();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-pwd@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-pwd@test.local");
        var email = new DevelopmentAccountEmailSender(
            new DevelopmentConfirmationTokenStore(),
            store,
            NullLogger<DevelopmentAccountEmailSender>.Instance);
        var sut = h.CreateServiceWithEmail(orgAdmin, email);

        var response = await sut.RequestPasswordResetAsync(doctor.Staff.Id, new StaffPasswordResetRequest());
        response.Message.Should().Contain("password reset");
        store.TryGet("doc-pwd@test.local", out var token).Should().BeTrue();
        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Permissions_Include_Password_Reset_And_Session_Revoke_For_Org_Admin()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().Contain(Permissions.Staff.PasswordReset)
            .And.Contain(Permissions.SecuritySessions.Revoke);
    }
}

internal sealed class StaffHarness : IAsyncDisposable
{
    public required HealthCareDbContext Db { get; init; }
    public required UserManager<ApplicationUser> Users { get; init; }
    public required Domain.Organizations.Organization Org { get; init; }
    public required Domain.Clinics.Clinic ClinicA { get; init; }
    public required Domain.Clinics.Clinic ClinicB { get; init; }
    private ServiceProvider? _provider;

    public static async Task<StaffHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>()
            .AddDefaultTokenProviders();
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));

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

        var org = new Domain.Organizations.Organization
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
            Name = "A",
            Slug = "a",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicB = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "B",
            Slug = "b",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new StaffHarness
        {
            Db = db,
            Users = users,
            Org = org,
            ClinicA = clinicA,
            ClinicB = clinicB,
            _provider = provider,
        };
    }

    public async Task<(ApplicationUser User, StaffMember Staff)> SeedStaffAsync(string role, Guid clinicId, string email)
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

        var staff = new StaffMember
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

    public StaffManagementService CreateService((ApplicationUser User, StaffMember Staff) actor) =>
        CreateServiceWithEmail(actor, new DevelopmentAccountEmailSender(
            new DevelopmentConfirmationTokenStore(),
            new DevelopmentPasswordResetTokenStore(),
            NullLogger<DevelopmentAccountEmailSender>.Instance));

    public StaffManagementService CreateServiceWithEmail(
        (ApplicationUser User, StaffMember Staff) actor,
        IAccountEmailSender emailSender)
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
        return BuildService(currentUser, currentStaff, emailSender);
    }

    public StaffManagementService CreatePlatformService(ApplicationUser platformUser)
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

    private StaffManagementService BuildService(
        FakeCurrentUser currentUser,
        FakeCurrentStaff currentStaff,
        IAccountEmailSender? emailSender = null)
    {
        var audit = new NoOpAuthorizationAuditLogger();
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            audit);
        var roleAssignment = new RoleAssignmentAuthorizationService(audit);
        var sessions = new SecuritySessionInvalidationService(Db, Users, NullLogger<SecuritySessionInvalidationService>.Instance);
        var email = emailSender ?? new DevelopmentAccountEmailSender(
            new DevelopmentConfirmationTokenStore(),
            new DevelopmentPasswordResetTokenStore(),
            NullLogger<DevelopmentAccountEmailSender>.Instance);

        return new StaffManagementService(
            Db,
            Users,
            currentUser,
            currentStaff,
            permissions,
            roleAssignment,
            sessions,
            email,
            audit,
            TimeProvider.System,
            NullLogger<StaffManagementService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}
