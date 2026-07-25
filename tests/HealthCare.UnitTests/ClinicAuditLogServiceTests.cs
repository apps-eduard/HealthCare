using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;

namespace HealthCare.UnitTests;

public sealed class ClinicAuditLogServiceTests
{
    [Fact]
    public void Clinic_Audit_Permission_Is_Granted_To_Clinic_And_Platform_Admin_Only()
    {
        Permissions.All.Should().Contain(Permissions.Clinics.AuditLogsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().Contain(Permissions.Clinics.AuditLogsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Clinics.AuditLogsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().NotContain(Permissions.Clinics.AuditLogsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor)
            .Should().NotContain(Permissions.Clinics.AuditLogsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Nurse)
            .Should().NotContain(Permissions.Clinics.AuditLogsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Receptionist)
            .Should().NotContain(Permissions.Clinics.AuditLogsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Patient)
            .Should().NotContain(Permissions.Clinics.AuditLogsRead);
    }

    [Fact]
    public async Task Clinic_Admin_Sees_Allowlisted_Own_Clinic_Events_Only()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit@test.local");

        var allowedId = Guid.NewGuid();
        var otherClinicId = Guid.NewGuid();
        var nonAllowlistedId = Guid.NewGuid();
        var unknownActionId = Guid.NewGuid();

        h.Db.OrganizationAuditEvents.AddRange(
            new OrganizationAuditEvent
            {
                Id = allowedId,
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicA.Id,
                ActorUserId = clinicAdmin.User.Id,
                Category = "clinic",
                Action = ClinicAuditActions.All[0],
                ResultCode = "succeeded",
                ResourceType = "clinic",
                ResourceId = h.ClinicA.Id,
                CorrelationId = "corr-allowed",
                OccurredAtUtc = h.Clock.GetUtcNow().AddMinutes(-10),
            },
            new OrganizationAuditEvent
            {
                Id = otherClinicId,
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicB.Id,
                ActorUserId = clinicAdmin.User.Id,
                Category = "clinic",
                Action = "clinic_profile_update",
                ResultCode = "succeeded",
                CorrelationId = "corr-other-clinic",
                OccurredAtUtc = h.Clock.GetUtcNow().AddMinutes(-9),
            },
            new OrganizationAuditEvent
            {
                Id = nonAllowlistedId,
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicA.Id,
                ActorUserId = clinicAdmin.User.Id,
                Category = "security",
                Action = "organization_profile_update",
                ResultCode = "succeeded",
                CorrelationId = "corr-non-allowlisted",
                OccurredAtUtc = h.Clock.GetUtcNow().AddMinutes(-8),
            },
            new OrganizationAuditEvent
            {
                Id = unknownActionId,
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicA.Id,
                ActorUserId = clinicAdmin.User.Id,
                Category = "clinic",
                Action = "future_unknown_action",
                ResultCode = "succeeded",
                CorrelationId = "corr-unknown",
                OccurredAtUtc = h.Clock.GetUtcNow().AddMinutes(-7),
            });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAuditService(clinicAdmin);
        var result = await sut.SearchAsync(new ClinicAuditLogQuery());

        result.ClinicId.Should().Be(h.ClinicA.Id);
        result.Items.Should().ContainSingle(i => i.AuditLogId == allowedId);
        result.Items.Should().NotContain(i => i.AuditLogId == otherClinicId);
        result.Items.Should().NotContain(i => i.AuditLogId == nonAllowlistedId);
        result.Items.Should().NotContain(i => i.AuditLogId == unknownActionId);
        result.Items[0].Action.Should().Be("clinic_profile_update");
        result.Items[0].Summary.Should().Be("Clinic profile updated");
        result.RetentionDays.Should().Be(365);
    }

    [Fact]
    public async Task Cross_Clinic_Query_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit-cross@test.local");
        var sut = h.CreateAuditService(clinicAdmin);

        var act = () => sut.SearchAsync(new ClinicAuditLogQuery { ClinicId = h.ClinicB.Id });
        await act.Should().ThrowAsync<ClinicAuditLogException>()
            .Where(e => e.ErrorCode == ClinicAuditLogErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Organization_Admin_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-clinic-audit@test.local");
        var sut = h.CreateAuditService(orgAdmin);

        var act = () => sut.SearchAsync(new ClinicAuditLogQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Inactive_Membership_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit-inactive@test.local");
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
        var sut = h.BuildAuditService(currentUser, currentStaff);

        var act = () => sut.SearchAsync(new ClinicAuditLogQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_ClinicId()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("plat-clinic-audit@test.local");
        var sut = h.CreatePlatformAuditService(platform);

        h.Db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = h.Org.Id,
            ClinicId = h.ClinicA.Id,
            ActorUserId = platform.Id,
            Category = "clinic",
            Action = "clinic_profile_update",
            ResultCode = "succeeded",
            OccurredAtUtc = h.Clock.GetUtcNow(),
        });
        await h.Db.SaveChangesAsync();

        var withoutBypass = () => sut.SearchAsync(new ClinicAuditLogQuery { ClinicId = h.ClinicA.Id });
        await withoutBypass.Should().ThrowAsync<AuthorizationException>();

        var withoutClinic = () => sut.SearchAsync(new ClinicAuditLogQuery(), PlatformAdminBypass.Explicit);
        await withoutClinic.Should().ThrowAsync<ClinicAuditLogException>()
            .Where(e => e.ErrorCode == ClinicAuditLogErrorCodes.ClinicScopeRequired);

        var invalidClinic = () => sut.SearchAsync(
            new ClinicAuditLogQuery { ClinicId = Guid.NewGuid() },
            PlatformAdminBypass.Explicit);
        await invalidClinic.Should().ThrowAsync<ClinicAuditLogException>()
            .Where(e => e.ErrorCode == ClinicAuditLogErrorCodes.ClinicNotFound);

        var ok = await sut.SearchAsync(
            new ClinicAuditLogQuery { ClinicId = h.ClinicA.Id },
            PlatformAdminBypass.Explicit);
        ok.ClinicId.Should().Be(h.ClinicA.Id);
        ok.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pagination_Bounds_Clamp_PageSize()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit-page@test.local");
        for (var i = 0; i < 3; i++)
        {
            h.Db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
            {
                Id = Guid.NewGuid(),
                OrganizationId = h.Org.Id,
                ClinicId = h.ClinicA.Id,
                ActorUserId = clinicAdmin.User.Id,
                Category = "staff",
                Action = "staff_created",
                ResultCode = "succeeded",
                OccurredAtUtc = h.Clock.GetUtcNow().AddMinutes(-i),
            });
        }

        await h.Db.SaveChangesAsync();
        var sut = h.CreateAuditService(clinicAdmin);

        var oversized = await sut.SearchAsync(new ClinicAuditLogQuery { PageSize = 500 });
        oversized.PageSize.Should().Be(ClinicAuditLogQueryValidator.MaxPageSize);

        var undersized = await sut.SearchAsync(new ClinicAuditLogQuery { PageSize = 0 });
        undersized.PageSize.Should().Be(ClinicAuditLogQueryValidator.DefaultPageSize);
    }

    [Fact]
    public async Task Date_Range_Over_93_Days_Is_Rejected()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit-range@test.local");
        var sut = h.CreateAuditService(clinicAdmin);

        var act = () => sut.SearchAsync(new ClinicAuditLogQuery
        {
            FromDate = "2026-01-01",
            ToDate = "2026-04-05",
        });
        await act.Should().ThrowAsync<ClinicAuditLogException>()
            .Where(e => e.ErrorCode == ClinicAuditLogErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task From_After_To_Is_Rejected()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit-fromto@test.local");
        var sut = h.CreateAuditService(clinicAdmin);

        var act = () => sut.SearchAsync(new ClinicAuditLogQuery
        {
            FromDate = "2026-07-10",
            ToDate = "2026-07-01",
        });
        await act.Should().ThrowAsync<ClinicAuditLogException>()
            .Where(e => e.ErrorCode == ClinicAuditLogErrorCodes.InvalidDateRange);
    }

    [Fact]
    public void Contracts_Have_No_Metadata_Password_Token_Or_Export_Surface()
    {
        AssertNoSensitiveMembers(typeof(ClinicAuditLogItem));
        AssertNoSensitiveMembers(typeof(ClinicAuditLogListResponse));
        AssertNoSensitiveMembers(typeof(ClinicAuditLogDetailResponse));
        AssertNoSensitiveMembers(typeof(ClinicAuditLogQuery));

        typeof(IClinicAuditLogService).GetMethods()
            .Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Export", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Csv", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Serialized_Responses_Do_Not_Contain_Sensitive_Fields()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-audit-safe@test.local");
        var eventId = Guid.NewGuid();
        h.Db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            Id = eventId,
            OrganizationId = h.Org.Id,
            ClinicId = h.ClinicA.Id,
            ActorUserId = clinicAdmin.User.Id,
            Category = "clinic",
            Action = "clinic_profile_update",
            ResultCode = "succeeded",
            CorrelationId = "corr-safe",
            OccurredAtUtc = h.Clock.GetUtcNow(),
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateAuditService(clinicAdmin);
        var list = await sut.SearchAsync(new ClinicAuditLogQuery());
        var detail = await sut.GetByIdAsync(eventId, new ClinicAuditLogQuery());

        foreach (var json in new[] { JsonSerializer.Serialize(list), JsonSerializer.Serialize(detail) })
        {
            json.Should().NotContain("Metadata");
            json.Should().NotContain("Password");
            json.Should().NotContain("Token");
            json.Should().NotContain("MedicalNote");
            json.ToLowerInvariant().Should().NotContain("billing");
        }
    }

    private static void AssertNoSensitiveMembers(Type type)
    {
        var names = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToList();
        names.Should().NotContain(n =>
            n.Contains("Metadata", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Token", StringComparison.OrdinalIgnoreCase)
            || n.Contains("MedicalNote", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Billing", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Export", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Csv", StringComparison.OrdinalIgnoreCase));
    }
}
