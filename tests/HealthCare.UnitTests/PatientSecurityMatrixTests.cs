using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Authorization;

namespace HealthCare.UnitTests;

/// <summary>
/// PM-7: Patient-focused permission catalog denials (complements DR-9 CrossRoleAuthorizationMatrixTests).
/// Ownership / authz-before-conflict service Facts remain in PatientScheduleMutationCutoffTests.
/// </summary>
public sealed class PatientSecurityMatrixTests
{
    public static TheoryData<string> PatientForbiddenPermissions()
    {
        var data = new TheoryData<string>();
        foreach (var permission in new[]
                 {
                     Permissions.Patients.Search,
                     Permissions.Patients.UpdateClinicStatus,
                     Permissions.MedicalNotes.Read,
                     Permissions.MedicalNotes.Create,
                     Permissions.MedicalNotes.UpdateDraft,
                     Permissions.MedicalNotes.Sign,
                     Permissions.MedicalNotes.Amend,
                     Permissions.Appointments.Confirm,
                     Permissions.Appointments.CheckIn,
                     Permissions.Appointments.Complete,
                     Permissions.Appointments.NoShow,
                     Permissions.Availability.ManageSelf,
                     Permissions.Availability.ManageClinic,
                     Permissions.Availability.ManageOrganization,
                     Permissions.Clinics.ReportsRead,
                     Permissions.Clinics.AuditLogsRead,
                     Permissions.Clinics.DashboardRead,
                     Permissions.Clinics.ProfileUpdate,
                     Permissions.Clinics.Manage,
                     Permissions.Organizations.DashboardRead,
                     Permissions.Organizations.ReportsRead,
                     Permissions.Organizations.AuditLogsRead,
                     Permissions.Organizations.ProfileUpdate,
                     Permissions.Doctors.DashboardRead,
                     Permissions.Doctors.ProfileUpdate,
                     Permissions.Staff.Manage,
                     Permissions.Staff.Read,
                     Permissions.Reminders.Read,
                     Permissions.Reminders.Retry,
                     Permissions.Summaries.Read,
                     Permissions.Hangfire.Dashboard,
                 })
        {
            data.Add(permission);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PatientForbiddenPermissions))]
    public void Patient_Role_Is_Denied_Staff_And_Clinical_Permissions(string permission)
    {
        RolePermissionMatrix.RoleHasPermission(AppRoles.Patient, permission).Should().BeFalse(
            because: $"PATIENT must not receive {permission}");

        CreatePermissionService(AppRoles.Patient).HasPermission(permission).Should().BeFalse();
    }

    [Fact]
    public void Patient_Retains_Self_Service_Permissions_Only()
    {
        var sut = CreatePermissionService(AppRoles.Patient);
        sut.HasPermission(Permissions.Patients.UpdateOwnProfile).Should().BeTrue();
        sut.HasPermission(Permissions.Patients.Read).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Create).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Read).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Cancel).Should().BeTrue();
        sut.HasPermission(Permissions.Appointments.Reschedule).Should().BeTrue();
        sut.HasPermission(Permissions.Availability.Read).Should().BeTrue();
        sut.HasPermission(Permissions.Clinics.Read).Should().BeTrue();
    }

    [Fact]
    public void Clinic_Scoped_Staff_Do_Not_Receive_Patient_UpdateOwnProfile()
    {
        // Patient self-service also requires PatientSelfScope + PATIENT linkage at the endpoint.
        foreach (var role in new[]
                 {
                     AppRoles.Doctor,
                     AppRoles.ClinicAdmin,
                     AppRoles.OrganizationAdmin,
                     AppRoles.Nurse,
                     AppRoles.Receptionist,
                 })
        {
            CreatePermissionService(role).HasPermission(Permissions.Patients.UpdateOwnProfile)
                .Should().BeFalse(because: $"{role} must not use Patient self-profile permission");
        }
    }

    private static IPermissionService CreatePermissionService(string role)
    {
        var isPatient = role == AppRoles.Patient;
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [role],
            PatientId = isPatient ? Guid.NewGuid() : null,
        };
        var staff = isPatient
            ? new FakeCurrentStaff()
            : new FakeCurrentStaff
            {
                HasActiveMembership = true,
                StaffMemberId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                ClinicId = Guid.NewGuid(),
                Role = role,
            };
        var patient = new FakeCurrentPatient
        {
            HasLinkedPatient = isPatient,
            PatientId = user.PatientId,
        };
        return new PermissionService(user, staff, patient, new NoOpAuthorizationAuditLogger());
    }
}
