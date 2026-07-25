using System.Text.Json;
using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Doctors;
using HealthCare.Contracts.Doctors;
using HealthCare.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.UnitTests;

public sealed class DoctorProfileServiceTests
{
    [Fact]
    public void Doctor_Profile_Permissions_Are_Granted_To_Doctor_And_Platform_Admin_Only()
    {
        Permissions.All.Should().Contain(Permissions.Doctors.ProfileRead);
        Permissions.All.Should().Contain(Permissions.Doctors.ProfileUpdate);

        foreach (var permission in new[] { Permissions.Doctors.ProfileRead, Permissions.Doctors.ProfileUpdate })
        {
            RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor).Should().Contain(permission);
            RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin).Should().Contain(permission);
            RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin).Should().NotContain(permission);
            RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin).Should().NotContain(permission);
            RolePermissionMatrix.GetPermissionsForRole(AppRoles.Nurse).Should().NotContain(permission);
            RolePermissionMatrix.GetPermissionsForRole(AppRoles.Receptionist).Should().NotContain(permission);
            RolePermissionMatrix.GetPermissionsForRole(AppRoles.Patient).Should().NotContain(permission);
        }
    }

    [Fact]
    public void Update_Request_Specified_Flags_Require_Explicit_Setters()
    {
        var empty = new UpdateDoctorProfileRequest { ExpectedVersion = 0 };
        empty.HasAnyEditableField.Should().BeFalse();
        empty.DisplayNameSpecified.Should().BeFalse();
        empty.ContactPhoneSpecified.Should().BeFalse();

        var partial = new UpdateDoctorProfileRequest { ExpectedVersion = 1, DisplayName = "Dr. Ada" };
        partial.DisplayNameSpecified.Should().BeTrue();
        partial.FirstNameSpecified.Should().BeFalse();
        partial.HasAnyEditableField.Should().BeTrue();

        var json = """{"expectedVersion":2,"displayName":null}""";
        var deserialized = JsonSerializer.Deserialize<UpdateDoctorProfileRequest>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        deserialized.Should().NotBeNull();
        deserialized!.DisplayNameSpecified.Should().BeTrue();
        deserialized.FirstNameSpecified.Should().BeFalse();
        deserialized.HasAnyEditableField.Should().BeTrue();
    }

    [Fact]
    public async Task Doctor_Gets_Own_Profile_With_Read_Only_Context()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        h.ClinicA.Specialty = "Cardiology";
        await h.Db.SaveChangesAsync();

        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-profile@test.local");
        doctor.Staff.DisplayName = "Dr. Own";
        doctor.Staff.JobTitle = "Consultant";
        doctor.User.PhoneNumber = "+966500000001";
        await h.Db.SaveChangesAsync();

        var sut = h.CreateDoctorProfileService(doctor);
        var result = await sut.GetAsync(new DoctorProfileQuery());

        result.StaffMemberId.Should().Be(doctor.Staff.Id);
        result.ClinicId.Should().Be(h.ClinicA.Id);
        result.OrganizationId.Should().Be(h.Org.Id);
        result.OrganizationName.Should().Be(h.Org.Name);
        result.ClinicName.Should().Be(h.ClinicA.Name);
        result.Email.Should().Be(doctor.User.Email);
        result.Role.Should().Be(AppRoles.Doctor);
        result.DisplayName.Should().Be("Dr. Own");
        result.JobTitle.Should().Be("Consultant");
        result.ContactPhone.Should().Be("+966500000001");
        result.Specialty.Should().Be("Cardiology");
        result.IsActive.Should().BeTrue();

        var json = JsonSerializer.Serialize(result);
        json.Should().NotContain("Subjective");
        json.Should().NotContain("Assessment");
        json.Should().NotContain("ActiveStaffCount");
        json.ToLowerInvariant().Should().NotContain("billing");
    }

    [Fact]
    public async Task Doctor_Cannot_Select_Another_Doctor_Or_Clinic()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctorA = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-prof-a@test.local");
        var doctorB = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-prof-b@test.local");
        var sut = h.CreateDoctorProfileService(doctorA);

        await FluentActions.Awaiting(() => sut.GetAsync(new DoctorProfileQuery { ClinicId = h.ClinicB.Id }))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.InvalidScope);

        await FluentActions.Awaiting(() => sut.GetAsync(new DoctorProfileQuery
            {
                DoctorStaffMemberId = doctorB.Staff.Id,
            }))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Inactive_And_Non_Doctor_Are_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var inactive = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-inactive-prof@test.local");
        inactive.Staff.IsActive = false;
        await h.Db.SaveChangesAsync();

        var nurse = await h.SeedStaffAsync(AppRoles.Nurse, h.ClinicA.Id, "nurse-prof@test.local");
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-prof@test.local");

        await FluentActions.Awaiting(() => h.CreateDoctorProfileService(inactive).GetAsync(new DoctorProfileQuery()))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => h.CreateDoctorProfileService(nurse).GetAsync(new DoctorProfileQuery()))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => h.CreateDoctorProfileService(clinicAdmin).GetAsync(new DoctorProfileQuery()))
            .Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_Clinic_And_Doctor()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-pa-prof@test.local");
        var platform = await h.SeedPlatformAdminAsync("plat-doc-prof@test.local");
        var sut = h.CreatePlatformDoctorProfileService(platform);

        await FluentActions.Awaiting(() => sut.GetAsync(new DoctorProfileQuery
            {
                ClinicId = h.ClinicA.Id,
                DoctorStaffMemberId = doctor.Staff.Id,
            }))
            .Should().ThrowAsync<AuthorizationException>();

        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorProfileQuery { DoctorStaffMemberId = doctor.Staff.Id },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.ClinicScopeRequired);

        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorProfileQuery { ClinicId = h.ClinicA.Id },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.DoctorScopeRequired);

        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorProfileQuery
                {
                    ClinicId = Guid.NewGuid(),
                    DoctorStaffMemberId = doctor.Staff.Id,
                },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.ClinicNotFound);

        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorProfileQuery
                {
                    ClinicId = h.ClinicA.Id,
                    DoctorStaffMemberId = Guid.NewGuid(),
                },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.DoctorNotFound);

        var otherClinicDoctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-other-clinic@test.local");
        await FluentActions.Awaiting(() => sut.GetAsync(
                new DoctorProfileQuery
                {
                    ClinicId = h.ClinicA.Id,
                    DoctorStaffMemberId = otherClinicDoctor.Staff.Id,
                },
                PlatformAdminBypass.Explicit))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.DoctorNotFound);

        var ok = await sut.GetAsync(
            new DoctorProfileQuery
            {
                ClinicId = h.ClinicA.Id,
                DoctorStaffMemberId = doctor.Staff.Id,
            },
            PlatformAdminBypass.Explicit);
        ok.StaffMemberId.Should().Be(doctor.Staff.Id);
    }

    [Fact]
    public async Task Patch_Updates_Approved_Fields_Only_And_Audits_Safe_Names()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        h.ClinicA.Specialty = "Dermatology";
        await h.Db.SaveChangesAsync();

        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-patch@test.local");
        doctor.Staff.FirstName = "Ada";
        doctor.Staff.LastName = "Lovelace";
        doctor.Staff.DisplayName = "Dr. Ada";
        doctor.Staff.JobTitle = "Surgeon";
        doctor.User.PhoneNumber = "+966511111111";
        await h.Db.SaveChangesAsync();

        var audit = new RecordingDoctorProfileAuditLogger();
        var sut = h.CreateDoctorProfileService(doctor, audit);
        var expectedVersion = doctor.Staff.Version;

        await FluentActions.Awaiting(() => sut.UpdateAsync(
                new UpdateDoctorProfileRequest { ExpectedVersion = expectedVersion },
                new DoctorProfileQuery()))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.EmptyUpdate);

        var updated = await sut.UpdateAsync(
            new UpdateDoctorProfileRequest
            {
                ExpectedVersion = expectedVersion,
                DisplayName = "Dr. Ada Updated",
                ContactPhone = "+966522222222",
            },
            new DoctorProfileQuery());

        updated.DisplayName.Should().Be("Dr. Ada Updated");
        updated.ContactPhone.Should().Be("+966522222222");
        updated.FirstName.Should().Be("Ada");
        updated.LastName.Should().Be("Lovelace");
        updated.JobTitle.Should().Be("Surgeon");
        updated.Email.Should().Be(doctor.User.Email);
        updated.Role.Should().Be(AppRoles.Doctor);
        updated.ClinicId.Should().Be(h.ClinicA.Id);
        updated.Specialty.Should().Be("Dermatology");
        updated.IsActive.Should().BeTrue();
        updated.Version.Should().Be(expectedVersion + 1);

        var auditEntry = audit.Operations.Single(o => o.Operation == "doctor_profile_update");
        auditEntry.ChangedFields.Should().NotBeNull();
        auditEntry.ChangedFields!.Should().Contain("DisplayName");
        auditEntry.ChangedFields.Should().Contain("ContactPhone");
        string.Join(',', auditEntry.ChangedFields).Should().NotContain("+966");
        string.Join(',', auditEntry.ChangedFields).Should().NotContain(doctor.User.Email!);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Conflict()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-conflict@test.local");
        var sut = h.CreateDoctorProfileService(doctor);

        await FluentActions.Awaiting(() => sut.UpdateAsync(
                new UpdateDoctorProfileRequest
                {
                    ExpectedVersion = doctor.Staff.Version + 5,
                    DisplayName = "Stale",
                },
                new DoctorProfileQuery()))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.ConcurrencyConflict
                        && e.StatusCode == 409);
    }

    [Fact]
    public async Task Omitted_Fields_Remain_Unchanged_On_Partial_Patch()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-partial@test.local");
        doctor.Staff.FirstName = "Grace";
        doctor.Staff.LastName = "Hopper";
        doctor.Staff.DisplayName = "Rear Admiral";
        doctor.Staff.JobTitle = "Engineer";
        doctor.User.PhoneNumber = "+966533333333";
        await h.Db.SaveChangesAsync();

        var sut = h.CreateDoctorProfileService(doctor);
        var updated = await sut.UpdateAsync(
            new UpdateDoctorProfileRequest
            {
                ExpectedVersion = doctor.Staff.Version,
                JobTitle = "Commodore",
            },
            new DoctorProfileQuery());

        updated.JobTitle.Should().Be("Commodore");
        updated.FirstName.Should().Be("Grace");
        updated.LastName.Should().Be("Hopper");
        updated.DisplayName.Should().Be("Rear Admiral");
        updated.ContactPhone.Should().Be("+966533333333");
    }

    [Fact]
    public async Task Invalid_ContactPhone_Length_Is_Rejected()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-phone@test.local");
        var sut = h.CreateDoctorProfileService(doctor);

        await FluentActions.Awaiting(() => sut.UpdateAsync(
                new UpdateDoctorProfileRequest
                {
                    ExpectedVersion = doctor.Staff.Version,
                    ContactPhone = new string('9', 31),
                },
                new DoctorProfileQuery()))
            .Should().ThrowAsync<DoctorProfileException>()
            .Where(e => e.ErrorCode == DoctorProfileErrorCodes.InvalidField && e.StatusCode == 400);
    }

    [Fact]
    public void Validator_Rejects_Invalid_Lengths()
    {
        var validator = new UpdateDoctorProfileRequestValidator();

        validator.Validate(new UpdateDoctorProfileRequest
            {
                ExpectedVersion = 0,
                FirstName = new string('a', 101),
            }).IsValid.Should().BeFalse();

        validator.Validate(new UpdateDoctorProfileRequest
            {
                ExpectedVersion = 0,
                LastName = new string('b', 101),
            }).IsValid.Should().BeFalse();

        validator.Validate(new UpdateDoctorProfileRequest
            {
                ExpectedVersion = 0,
                DisplayName = new string('c', 201),
            }).IsValid.Should().BeFalse();

        validator.Validate(new UpdateDoctorProfileRequest
            {
                ExpectedVersion = 0,
                JobTitle = new string('d', 151),
            }).IsValid.Should().BeFalse();

        validator.Validate(new UpdateDoctorProfileRequest
            {
                ExpectedVersion = 0,
                ContactPhone = new string('1', 31),
            }).IsValid.Should().BeFalse();

        validator.Validate(new UpdateDoctorProfileRequest
            {
                ExpectedVersion = 0,
                DisplayName = "Ok",
            }).IsValid.Should().BeTrue();
    }
}

internal sealed class RecordingDoctorProfileAuditLogger : NoOpAuthorizationAuditLogger
{
    public List<(string Operation, string ResultCode, Guid? OrganizationId, Guid? ClinicId, Guid? StaffMemberId, IReadOnlyList<string>? ChangedFields)> Operations { get; } = [];

    public override void StaffOperation(
        string operation,
        string resultCode,
        Guid? organizationId = null,
        Guid? clinicId = null,
        Guid? staffMemberId = null,
        IReadOnlyList<string>? changedFields = null)
    {
        Operations.Add((operation, resultCode, organizationId, clinicId, staffMemberId, changedFields));
    }
}
