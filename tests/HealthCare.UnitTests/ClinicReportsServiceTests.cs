using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;

namespace HealthCare.UnitTests;

public sealed class ClinicReportsServiceTests
{
    [Fact]
    public void Clinic_Reports_Permission_Is_Granted_To_Clinic_And_Platform_Admin_Only()
    {
        Permissions.All.Should().Contain(Permissions.Clinics.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().Contain(Permissions.Clinics.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Clinics.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().NotContain(Permissions.Clinics.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Doctor)
            .Should().NotContain(Permissions.Clinics.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Nurse)
            .Should().NotContain(Permissions.Clinics.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Receptionist)
            .Should().NotContain(Permissions.Clinics.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.Patient)
            .Should().NotContain(Permissions.Clinics.ReportsRead);
    }

    [Fact]
    public async Task Clinic_Admin_Resolves_Membership_Clinic_And_Excludes_Other_Clinic()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-rep@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-a-rep@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-b-rep@test.local");
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Completed);
        await h.SeedAppointmentAsync(h.ClinicB.Id, AppointmentStatus.NoShow);

        var sut = h.CreateReportService(clinicAdmin);
        var result = await sut.GetAppointmentsAsync(new ClinicReportQuery());

        result.Context.ClinicId.Should().Be(h.ClinicA.Id);
        result.Context.TimeZoneStrategy.Should().Be("clinic");
        result.Context.TimeZoneId.Should().Be("Asia/Riyadh");
        result.TotalAppointments.Should().Be(2);
        result.ByStatus.Should().NotContain(s => s.Status == nameof(AppointmentStatus.NoShow));
    }

    [Fact]
    public async Task Cross_Clinic_Query_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-cross@test.local");
        var sut = h.CreateReportService(clinicAdmin);

        var act = () => sut.GetAppointmentsAsync(new ClinicReportQuery { ClinicId = h.ClinicB.Id });
        await act.Should().ThrowAsync<ClinicReportException>()
            .Where(e => e.ErrorCode == ClinicReportErrorCodes.InvalidScope);
    }

    [Fact]
    public async Task Inactive_Membership_Is_Denied()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-rep-inactive@test.local");
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
        var sut = h.BuildReportService(currentUser, currentStaff);

        var act = () => sut.GetAppointmentsAsync(new ClinicReportQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_ClinicId()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("plat-clinic-rep@test.local");
        var sut = h.CreatePlatformReportService(platform);

        var withoutBypass = () => sut.GetAppointmentsAsync(new ClinicReportQuery { ClinicId = h.ClinicA.Id });
        await withoutBypass.Should().ThrowAsync<AuthorizationException>();

        var withoutClinic = () => sut.GetAppointmentsAsync(new ClinicReportQuery(), PlatformAdminBypass.Explicit);
        await withoutClinic.Should().ThrowAsync<ClinicReportException>()
            .Where(e => e.ErrorCode == ClinicReportErrorCodes.ClinicScopeRequired);

        var invalidClinic = () => sut.GetAppointmentsAsync(
            new ClinicReportQuery { ClinicId = Guid.NewGuid() },
            PlatformAdminBypass.Explicit);
        await invalidClinic.Should().ThrowAsync<ClinicReportException>()
            .Where(e => e.ErrorCode == ClinicReportErrorCodes.ClinicNotFound);

        var ok = await sut.GetAppointmentsAsync(
            new ClinicReportQuery { ClinicId = h.ClinicA.Id },
            PlatformAdminBypass.Explicit);
        ok.Context.ClinicId.Should().Be(h.ClinicA.Id);
    }

    [Fact]
    public async Task Date_Range_Over_93_Days_Is_Rejected()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-range@test.local");
        var sut = h.CreateReportService(clinicAdmin);

        var act = () => sut.GetAppointmentsAsync(new ClinicReportQuery
        {
            FromDate = "2026-01-01",
            ToDate = "2026-04-05",
        });
        await act.Should().ThrowAsync<ClinicReportException>()
            .Where(e => e.ErrorCode == ClinicReportErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task From_After_To_Is_Rejected()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-fromto@test.local");
        var sut = h.CreateReportService(clinicAdmin);

        var act = () => sut.GetAppointmentsAsync(new ClinicReportQuery
        {
            FromDate = "2026-07-10",
            ToDate = "2026-07-01",
        });
        await act.Should().ThrowAsync<ClinicReportException>()
            .Where(e => e.ErrorCode == ClinicReportErrorCodes.InvalidDateRange);
    }

    [Fact]
    public async Task Clinic_Local_Boundaries_And_Volume_Series_Are_Used()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-tz-rep@test.local");
        var converter = new ClinicTimeZoneConverter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClinicTimeZoneConverter>.Instance);
        var today = converter.GetClinicDate(h.Clock.GetUtcNow(), h.ClinicA.TimeZoneId);
        var yesterday = today.AddDays(-1);

        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.Confirmed, yesterday);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.Completed, today);

        var sut = h.CreateReportService(clinicAdmin);
        var result = await sut.GetAppointmentsAsync(new ClinicReportQuery
        {
            FromDate = yesterday.ToString("yyyy-MM-dd"),
            ToDate = today.ToString("yyyy-MM-dd"),
        });

        result.Context.FromDate.Should().Be(yesterday.ToString("yyyy-MM-dd"));
        result.Context.ToDate.Should().Be(today.ToString("yyyy-MM-dd"));
        result.VolumeByDate.Should().HaveCount(2);
        result.VolumeByDate[0].LocalDate.Should().Be(yesterday.ToString("yyyy-MM-dd"));
        result.VolumeByDate[0].AppointmentCount.Should().Be(1);
        result.VolumeByDate[1].LocalDate.Should().Be(today.ToString("yyyy-MM-dd"));
        result.VolumeByDate[1].CompletedCount.Should().Be(1);
        result.TotalAppointments.Should().Be(2);
    }

    [Fact]
    public async Task Appointment_Status_Cancellation_And_Doctor_Aggregates_Are_Correct()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-agg@test.local");
        var doctorA = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-agg-a@test.local");
        doctorA.Staff.DisplayName = "Dr Aggregate A";
        await h.Db.SaveChangesAsync();
        var doctorB = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-agg-b@test.local");
        doctorB.Staff.DisplayName = "Dr Aggregate B";
        await h.Db.SaveChangesAsync();

        var converter = new ClinicTimeZoneConverter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClinicTimeZoneConverter>.Instance);
        var day = converter.GetClinicDate(h.Clock.GetUtcNow(), h.ClinicA.TimeZoneId);

        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.Completed, day, doctorA.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.CancelledByClinic, day, doctorA.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.CancelledByPatient, day, doctorB.Staff.Id);
        await h.SeedAppointmentOnLocalDateAsync(h.ClinicA.Id, AppointmentStatus.NoShow, day, doctorB.Staff.Id);

        doctorB.Staff.IsActive = false;
        await h.Db.SaveChangesAsync();

        var sut = h.CreateReportService(clinicAdmin);
        var query = new ClinicReportQuery
        {
            FromDate = day.ToString("yyyy-MM-dd"),
            ToDate = day.ToString("yyyy-MM-dd"),
        };

        var appointments = await sut.GetAppointmentsAsync(query);
        appointments.TotalAppointments.Should().Be(4);
        appointments.ByStatus.Should().Contain(s => s.Status == nameof(AppointmentStatus.Completed) && s.Count == 1);
        appointments.CancellationNoShow.CancelledByClinicCount.Should().Be(1);
        appointments.CancellationNoShow.CancelledByPatientCount.Should().Be(1);
        appointments.CancellationNoShow.NoShowCount.Should().Be(1);
        appointments.CancellationNoShow.CancellationRate.Should().Be(50m);
        appointments.CancellationNoShow.NoShowRate.Should().Be(25m);

        var doctors = await sut.GetDoctorsAsync(query);
        doctors.Doctors.Should().HaveCount(2);
        doctors.Doctors.Should().Contain(d =>
            d.DoctorStaffMemberId == doctorA.Staff.Id
            && d.DoctorDisplayName == "Dr Aggregate A"
            && d.TotalAppointments == 2
            && d.CompletedCount == 1
            && d.CancelledCount == 1);
        doctors.Doctors.Should().Contain(d =>
            d.DoctorStaffMemberId == doctorB.Staff.Id
            && d.TotalAppointments == 2
            && d.NoShowCount == 1
            && d.CancelledCount == 1);
    }

    [Fact]
    public async Task Enrollment_And_Operations_Aggregates_Are_Clinic_Scoped()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-enroll@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-ops@test.local");
        await h.SeedPatientEnrollmentAsync(h.ClinicA.Id, active: true);
        await h.SeedPatientEnrollmentAsync(h.ClinicA.Id, active: false);
        await h.SeedPatientEnrollmentAsync(h.ClinicB.Id, active: true);

        // DbContext stamps ClinicPatient.RegisteredAtUtc with wall-clock UtcNow on insert.
        var converter = new ClinicTimeZoneConverter(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClinicTimeZoneConverter>.Instance);
        var today = converter.GetClinicDate(DateTimeOffset.UtcNow, h.ClinicA.TimeZoneId);
        var rangeStart = converter.ToUtc(today.AddDays(-1), TimeOnly.MinValue, h.ClinicA.TimeZoneId);

        await h.SeedReminderForClinicAsync(h.ClinicA.Id, AppointmentReminderStatus.Failed, rangeStart.AddHours(2));
        await h.SeedReminderForClinicAsync(h.ClinicA.Id, AppointmentReminderStatus.Sent, rangeStart.AddHours(3));
        await h.SeedReminderForClinicAsync(h.ClinicB.Id, AppointmentReminderStatus.Failed, rangeStart.AddHours(2));
        await h.SeedSummaryRunAsync(h.ClinicA.Id, ClinicAppointmentSummaryRunStatus.Failed, today);
        await h.SeedSummaryRunAsync(h.ClinicA.Id, ClinicAppointmentSummaryRunStatus.Pending, today.AddDays(-1));
        await h.SeedSummaryRunAsync(h.ClinicB.Id, ClinicAppointmentSummaryRunStatus.Failed, today);

        var sut = h.CreateReportService(clinicAdmin);
        var query = new ClinicReportQuery
        {
            FromDate = today.AddDays(-1).ToString("yyyy-MM-dd"),
            ToDate = today.ToString("yyyy-MM-dd"),
        };

        var patients = await sut.GetPatientsAsync(query);
        patients.ActiveEnrollmentCount.Should().BeGreaterThanOrEqualTo(1);
        patients.InactiveEnrollmentCount.Should().BeGreaterThanOrEqualTo(1);
        patients.TotalClinicPatients.Should().Be(patients.ActiveEnrollmentCount + patients.InactiveEnrollmentCount);
        patients.NewEnrollmentsInRange.Should().BeGreaterThanOrEqualTo(2);

        var ops = await sut.GetRemindersAsync(query);
        ops.FailedReminderCount.Should().Be(1);
        ops.SentReminderCount.Should().Be(1);
        ops.FailedSummaryRunCount.Should().Be(1);
        ops.PendingSummaryRunCount.Should().Be(1);
        ops.MissingActiveDoctorAvailability.Should().BeTrue();
    }

    [Fact]
    public void Contracts_Have_No_Patient_Billing_Or_Export_Surface()
    {
        AssertNoSensitiveMembers(typeof(ClinicAppointmentReportResponse));
        AssertNoSensitiveMembers(typeof(ClinicDoctorAppointmentsReportResponse));
        AssertNoSensitiveMembers(typeof(ClinicPatientEnrollmentReportResponse));
        AssertNoSensitiveMembers(typeof(ClinicOperationsReportResponse));
        AssertNoSensitiveMembers(typeof(ClinicDoctorAppointmentRow));
        AssertNoSensitiveMembers(typeof(ClinicCancellationNoShowSummary));

        typeof(IClinicReportsService).GetMethods()
            .Select(m => m.Name)
            .Should().NotContain(n => n.Contains("Export", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Csv", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Pdf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Serialized_Responses_Do_Not_Contain_Phi_Or_Billing_Fields()
    {
        await using var h = await ClinicDashHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-phi@test.local");
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);
        await h.SeedPatientEnrollmentAsync(h.ClinicA.Id, active: true);

        var sut = h.CreateReportService(clinicAdmin);
        var appointments = await sut.GetAppointmentsAsync(new ClinicReportQuery());
        var patients = await sut.GetPatientsAsync(new ClinicReportQuery());
        var doctors = await sut.GetDoctorsAsync(new ClinicReportQuery());
        var ops = await sut.GetRemindersAsync(new ClinicReportQuery());

        foreach (var json in new[]
                 {
                     JsonSerializer.Serialize(appointments),
                     JsonSerializer.Serialize(patients),
                     JsonSerializer.Serialize(doctors),
                     JsonSerializer.Serialize(ops),
                 })
        {
            json.Should().NotContain("PatientName");
            json.Should().NotContain("diagnosis");
            json.Should().NotContain("prescription");
            json.Should().NotContain("MaxClinics");
            json.Should().NotContain("billing");
            json.Should().NotContain("subscription");
            json.Should().NotContain("MedicalNote");
        }
    }

    private static void AssertNoSensitiveMembers(Type type)
    {
        var names = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => p.Name)
            .ToList();
        names.Should().NotContain(n =>
            n.Contains("PatientName", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Diagnosis", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Prescription", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Billing", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Revenue", StringComparison.OrdinalIgnoreCase)
            || n.Contains("MaxClinic", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Csv", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Export", StringComparison.OrdinalIgnoreCase));
    }
}
