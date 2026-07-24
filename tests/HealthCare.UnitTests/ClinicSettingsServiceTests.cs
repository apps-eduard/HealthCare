using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using FluentValidation.TestHelper;
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

public sealed class ClinicSettingsServiceTests
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Clinic_Profile_Permissions_Exist_And_Are_Granted_Correctly()
    {
        Permissions.All.Should().Contain(Permissions.Clinics.ProfileRead)
            .And.Contain(Permissions.Clinics.ProfileUpdate);

        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().Contain(Permissions.Clinics.ProfileRead)
            .And.Contain(Permissions.Clinics.ProfileUpdate);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Clinics.ProfileRead)
            .And.Contain(Permissions.Clinics.ProfileUpdate);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().NotContain(Permissions.Clinics.ProfileRead)
            .And.NotContain(Permissions.Clinics.ProfileUpdate);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor)
            .Should().NotContain(Permissions.Clinics.ProfileRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Nurse)
            .Should().NotContain(Permissions.Clinics.ProfileUpdate);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Receptionist)
            .Should().NotContain(Permissions.Clinics.ProfileRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Patient)
            .Should().NotContain(Permissions.Clinics.ProfileRead)
            .And.NotContain(Permissions.Clinics.ProfileUpdate);
    }

    [Fact]
    public void Empty_Patch_And_Field_Validation_Are_Rejected()
    {
        var validator = new UpdateClinicSettingsRequestValidator();

        validator.TestValidate(new UpdateClinicSettingsRequest { ExpectedVersion = 0 })
            .ShouldHaveValidationErrorFor(x => x)
            .WithErrorCode(ClinicSettingsErrorCodes.EmptyUpdate);

        validator.TestValidate(new UpdateClinicSettingsRequest { ExpectedVersion = 0, Name = " " })
            .ShouldHaveValidationErrorFor(x => x.Name);

        validator.TestValidate(new UpdateClinicSettingsRequest
            {
                ExpectedVersion = 0,
                ContactEmail = "not-an-email",
            })
            .ShouldHaveValidationErrorFor(x => x.ContactEmail);

        validator.TestValidate(new UpdateClinicSettingsRequest
            {
                ExpectedVersion = 0,
                DefaultTimeZoneId = " ",
            })
            .ShouldHaveValidationErrorFor(x => x.DefaultTimeZoneId);
    }

    [Fact]
    public void Omitted_And_Null_Json_Do_Not_Incorrectly_Mark_Fields_As_Specified()
    {
        var omitted = JsonSerializer.Deserialize<UpdateClinicSettingsRequest>(
            """{"expectedVersion":1}""",
            PatchJsonOptions)!;
        omitted.ExpectedVersion.Should().Be(1);
        omitted.NameSpecified.Should().BeFalse();
        omitted.ContactEmailSpecified.Should().BeFalse();
        omitted.HasAnyEditableField.Should().BeFalse();

        var roundTrip = JsonSerializer.Serialize(
            new UpdateClinicSettingsRequest { ExpectedVersion = 2, Name = "Only Name" },
            PatchJsonOptions);
        roundTrip.Should().Contain("name");
        roundTrip.Should().NotContain("contactEmail");
        roundTrip.Should().NotContain("specialty");

        var restored = JsonSerializer.Deserialize<UpdateClinicSettingsRequest>(roundTrip, PatchJsonOptions)!;
        restored.NameSpecified.Should().BeTrue();
        restored.SpecialtySpecified.Should().BeFalse();
        restored.ContactEmailSpecified.Should().BeFalse();
        restored.AddressSpecified.Should().BeFalse();

        // Explicit null in JSON still marks Specified (intentional clear); clients must use WhenWritingNull.
        var explicitNull = JsonSerializer.Deserialize<UpdateClinicSettingsRequest>(
            """{"expectedVersion":1,"contactEmail":null}""",
            PatchJsonOptions)!;
        explicitNull.ContactEmailSpecified.Should().BeTrue();
        explicitNull.ContactEmail.Should().BeNull();
    }

    [Fact]
    public async Task Clinic_Admin_Can_Read_And_Update_Own_Clinic()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        h.ClinicA.Email = "before@clinic.test";
        h.ClinicA.Specialty = "General";
        await h.Db.SaveChangesAsync();

        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-settings@test.local");
        var sut = h.CreateService(clinicAdmin);

        var before = await sut.GetAsync(new ClinicSettingsQuery());
        before.ClinicId.Should().Be(h.ClinicA.Id);
        before.OrganizationName.Should().Be(h.Org.Name);
        before.ContactEmail.Should().Be("before@clinic.test");
        before.Version.Should().Be(0);

        var updated = await sut.UpdateAsync(
            new UpdateClinicSettingsRequest
            {
                ExpectedVersion = before.Version,
                Name = "Clinic A Renamed",
                Specialty = "Cardiology",
                ContactEmail = "ops@clinic.test",
                ContactPhone = "+966500000001",
                Address = "King Fahd Rd",
                City = "Riyadh",
                Country = "SA",
                DefaultTimeZoneId = "Asia/Riyadh",
            },
            new ClinicSettingsQuery());

        updated.Name.Should().Be("Clinic A Renamed");
        updated.Specialty.Should().Be("Cardiology");
        updated.ContactEmail.Should().Be("ops@clinic.test");
        updated.ContactPhone.Should().Be("+966500000001");
        updated.Address.Should().Be("King Fahd Rd");
        updated.City.Should().Be("Riyadh");
        updated.Country.Should().Be("SA");
        updated.DefaultTimeZoneId.Should().Be("Asia/Riyadh");
        updated.Slug.Should().Be(h.ClinicA.Slug);
        updated.IsActive.Should().BeTrue();
        updated.Version.Should().Be(1);

        h.Audit.Operations.Should().Contain(o =>
            o.Operation == "clinic_profile_update"
            && o.ResultCode == "succeeded"
            && o.ClinicId == h.ClinicA.Id);
        var audit = h.Audit.Operations.Single(o => o.Operation == "clinic_profile_update");
        audit.ChangedFields.Should().NotBeNull();
        audit.ChangedFields!.Should().Contain("Name");
        audit.ChangedFields.Should().Contain("ContactEmail");
        string.Join(',', audit.ChangedFields).Should().NotContain("ops@clinic.test");
        string.Join(',', audit.ChangedFields).Should().NotContain("+966");
    }

    [Fact]
    public async Task Unspecified_Fields_Remain_Unchanged()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        h.ClinicA.Name = "Keep Name";
        h.ClinicA.Email = "keep@clinic.test";
        h.ClinicA.City = "Jeddah";
        await h.Db.SaveChangesAsync();

        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-partial@test.local");
        var sut = h.CreateService(clinicAdmin);

        var updated = await sut.UpdateAsync(
            new UpdateClinicSettingsRequest
            {
                ExpectedVersion = 0,
                Specialty = "Dermatology",
            },
            new ClinicSettingsQuery());

        updated.Name.Should().Be("Keep Name");
        updated.ContactEmail.Should().Be("keep@clinic.test");
        updated.City.Should().Be("Jeddah");
        updated.Specialty.Should().Be("Dermatology");
    }

    [Fact]
    public async Task Mismatched_Clinic_Is_Denied()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-scope@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.GetAsync(new ClinicSettingsQuery { ClinicId = h.ClinicB.Id });
        await act.Should().ThrowAsync<ClinicSettingsException>()
            .Where(e => e.ErrorCode == ClinicSettingsErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_ClinicId()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("plat-clinic-settings@test.local");
        var sut = h.CreatePlatformService(platform);

        await FluentActions.Awaiting(() => sut.GetAsync(new ClinicSettingsQuery { ClinicId = h.ClinicA.Id }))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => sut.GetAsync(new ClinicSettingsQuery(), PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<ClinicSettingsException>()
            .Where(e => e.ErrorCode == ClinicSettingsErrorCodes.ClinicScopeRequired);

        var ok = await sut.GetAsync(
            new ClinicSettingsQuery { ClinicId = h.ClinicA.Id },
            PlatformAdminBypass.Explicit);
        ok.ClinicId.Should().Be(h.ClinicA.Id);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Produces_Conflict()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-conc@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.UpdateAsync(
            new UpdateClinicSettingsRequest
            {
                ExpectedVersion = 99,
                Name = "Should Fail",
            },
            new ClinicSettingsQuery());

        await act.Should().ThrowAsync<ClinicSettingsException>()
            .Where(e => e.ErrorCode == ClinicSettingsErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task Invalid_Timezone_Is_Rejected()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-tz@test.local");
        var sut = h.CreateService(clinicAdmin);

        var act = () => sut.UpdateAsync(
            new UpdateClinicSettingsRequest
            {
                ExpectedVersion = 0,
                DefaultTimeZoneId = "Not/A_Real_Zone",
            },
            new ClinicSettingsQuery());

        await act.Should().ThrowAsync<ClinicSettingsException>()
            .Where(e => e.ErrorCode == ClinicSettingsErrorCodes.InvalidTimezone);
    }

    [Fact]
    public async Task Organization_Admin_And_Doctor_Are_Denied()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-clinic-settings@test.local");
        var orgSut = h.CreateService(orgAdmin);
        await FluentActions.Awaiting(() => orgSut.GetAsync(new ClinicSettingsQuery()))
            .Should().ThrowAsync<AuthorizationException>();

        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-clinic-settings@test.local");
        var docSut = h.CreateService(doctor);
        await FluentActions.Awaiting(() => docSut.GetAsync(new ClinicSettingsQuery()))
            .Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Inactive_Membership_Is_Denied()
    {
        await using var h = await ClinicSettingsHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-inactive-settings@test.local");
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

        var act = () => sut.GetAsync(new ClinicSettingsQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }
}

internal sealed class ClinicSettingsHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;

    public HealthCareDbContext Db { get; private set; } = null!;

    public UserManager<ApplicationUser> Users { get; private set; } = null!;

    public Organization Org { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicA { get; private set; } = null!;

    public Domain.Clinics.Clinic ClinicB { get; private set; } = null!;

    public RecordingClinicSettingsAuditLogger Audit { get; } = new();

    public TimeProvider Clock { get; } = TimeProvider.System;

    public static async Task<ClinicSettingsHarness> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>();
        services.AddDbContext<HealthCareDbContext>(o =>
            o.UseInMemoryDatabase("clinic-settings-" + Guid.NewGuid().ToString("N")));

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
            Name = "Clinic Settings Org",
            Slug = "clinic-settings-org",
            Status = OrganizationStatus.Active,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        var clinicA = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = org.Id,
            Name = "Clinic A",
            Slug = "clinic-a-settings",
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
            Slug = "clinic-b-settings",
            IsActive = true,
            TimeZoneId = "Asia/Riyadh",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Organizations.Add(org);
        db.Clinics.AddRange(clinicA, clinicB);
        await db.SaveChangesAsync();

        return new ClinicSettingsHarness
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

    public ClinicSettingsService CreateService((ApplicationUser User, Domain.Staff.StaffMember Staff) actor)
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

    public ClinicSettingsService CreatePlatformService(ApplicationUser platformUser)
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

    public ClinicSettingsService BuildService(FakeCurrentUser currentUser, FakeCurrentStaff currentStaff)
    {
        var permissions = new PermissionService(
            currentUser,
            currentStaff,
            new FakeCurrentPatient(),
            Audit);

        return new ClinicSettingsService(
            Db,
            currentUser,
            currentStaff,
            permissions,
            Audit,
            Clock,
            NullLogger<ClinicSettingsService>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }
    }
}

internal sealed class RecordingClinicSettingsAuditLogger : NoOpAuthorizationAuditLogger
{
    public List<(string Operation, string ResultCode, Guid? OrganizationId, Guid? ClinicId, IReadOnlyList<string>? ChangedFields)> Operations { get; } = [];

    public override void ClinicOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        IReadOnlyList<string>? changedFields = null)
    {
        Operations.Add((operation, resultCode, organizationId, clinicId, changedFields));
    }
}
