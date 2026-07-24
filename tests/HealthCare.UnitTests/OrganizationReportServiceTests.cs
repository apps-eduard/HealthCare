using System.Text;
using FluentAssertions;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Appointments;
using HealthCare.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class OrganizationReportServiceTests
{
    [Fact]
    public async Task Organization_Admin_Appointment_Report_Is_Organization_Scoped()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-rep@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-rep-a@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-rep-b@test.local");
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.NoShow);
        await h.SeedAppointmentAsync(h.ClinicB.Id, AppointmentStatus.CancelledByPatient);

        var sut = h.CreateReportService(orgAdmin);
        var result = await sut.GetAppointmentsAsync(new OrganizationReportQuery());

        result.Context.OrganizationId.Should().Be(h.Org.Id);
        result.Totals.TotalAppointments.Should().Be(3);
        result.Totals.NoShowCount.Should().Be(1);
        result.Totals.CancellationCount.Should().Be(1);
        result.ByClinic.Should().HaveCount(2);
        result.ByDoctor.Should().NotBeEmpty();
        result.ByStatus.Should().Contain(s => s.Status == nameof(AppointmentStatus.Confirmed) && s.Count == 1);
    }

    [Fact]
    public async Task Organization_Admin_Clinic_Filter_Scopes_Reports()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-rep-filter@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-f-a@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-f-b@test.local");
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Completed);
        await h.SeedAppointmentAsync(h.ClinicB.Id, AppointmentStatus.Completed);
        await h.SeedPatientEnrollmentAsync(h.ClinicA.Id, active: true);
        await h.SeedPatientEnrollmentAsync(h.ClinicB.Id, active: true);

        var sut = h.CreateReportService(orgAdmin);
        var appointments = await sut.GetAppointmentsAsync(new OrganizationReportQuery { ClinicId = h.ClinicA.Id });
        appointments.Totals.TotalAppointments.Should().Be(1);
        appointments.ByClinic.Should().ContainSingle(c => c.ClinicId == h.ClinicA.Id);
        appointments.Context.TimeZoneStrategy.Should().Be("clinic");

        var staff = await sut.GetStaffAsync(new OrganizationReportQuery { ClinicId = h.ClinicA.Id });
        staff.ByClinic.Should().ContainSingle();
        staff.ByClinic[0].DoctorCount.Should().Be(1);

        var patients = await sut.GetPatientsAsync(new OrganizationReportQuery { ClinicId = h.ClinicA.Id });
        patients.ByClinic.Should().ContainSingle(c => c.ClinicId == h.ClinicA.Id);
        patients.TotalActiveEnrollments.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Organization_Admin_Availability_And_Failure_Reports()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-avail@test.local");
        var doctor = await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-avail@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicB.Id, "doc-gap@test.local");

        h.Db.DoctorAvailabilities.Add(new DoctorAvailability
        {
            Id = Guid.NewGuid(),
            OrganizationId = h.Org.Id,
            ClinicId = h.ClinicA.Id,
            DoctorStaffMemberId = doctor.Staff.Id,
            DayOfWeek = DayOfWeek.Monday,
            StartLocalTime = new TimeOnly(9, 0),
            EndLocalTime = new TimeOnly(17, 0),
            SlotDurationMinutes = 30,
            EffectiveFrom = new DateOnly(2020, 1, 1),
            IsActive = true,
        });

        var apptId = Guid.NewGuid();
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);
        var appointment = await h.Db.Appointments.AsNoTracking().FirstAsync(a => a.ClinicId == h.ClinicA.Id);
        apptId = appointment.Id;
        h.Db.AppointmentReminders.Add(new AppointmentReminder
        {
            Id = Guid.NewGuid(),
            AppointmentId = apptId,
            ReminderType = AppointmentReminderType.Confirmation,
            ScheduledAtUtc = h.Clock.GetUtcNow(),
            Status = AppointmentReminderStatus.Failed,
            AttemptCount = 2,
            LastError = "simulated",
            IdempotencyKey = AppointmentReminder.BuildIdempotencyKey(apptId, AppointmentReminderType.Confirmation),
            BackgroundJobId = "job-fail",
            CreatedAtUtc = h.Clock.GetUtcNow(),
            UpdatedAtUtc = h.Clock.GetUtcNow(),
        });

        var summaryDate = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance)
            .GetClinicDate(h.Clock.GetUtcNow(), h.ClinicA.TimeZoneId);
        h.Db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
        {
            Id = Guid.NewGuid(),
            ClinicId = h.ClinicB.Id,
            OrganizationId = h.Org.Id,
            SummaryDate = summaryDate,
            ScheduledAtUtc = h.Clock.GetUtcNow(),
            Status = ClinicAppointmentSummaryRunStatus.Failed,
            AttemptCount = 1,
            LastErrorCode = AppointmentSummaryErrorCodes.SummaryDeliveryFailed,
            IdempotencyKey = ClinicAppointmentSummaryRun.BuildIdempotencyKey(h.ClinicB.Id, summaryDate),
            BackgroundJobId = "sum-fail",
        });
        await h.Db.SaveChangesAsync();

        var sut = h.CreateReportService(orgAdmin);
        var availability = await sut.GetAvailabilityAsync(new OrganizationReportQuery());
        availability.ByClinic.Should().Contain(c => c.ClinicId == h.ClinicA.Id && !c.HasCoverageGap);
        availability.ByClinic.Should().Contain(c => c.ClinicId == h.ClinicB.Id && c.HasCoverageGap);

        var reminders = await sut.GetReminderFailuresAsync(new OrganizationReportQuery());
        reminders.FailedCount.Should().Be(1);
        reminders.Items.Should().ContainSingle(i => i.BackgroundJobId == "job-fail");

        var summaries = await sut.GetSummaryFailuresAsync(new OrganizationReportQuery());
        summaries.FailedCount.Should().Be(1);
        summaries.Items.Should().ContainSingle(i => i.ClinicId == h.ClinicB.Id);
    }

    [Fact]
    public async Task Organization_Admin_Csv_Export_Is_Safe_Operational_Data()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-csv@test.local");
        await h.SeedStaffAsync(AppRoles.Doctor, h.ClinicA.Id, "doc-csv@test.local");
        await h.SeedAppointmentAsync(h.ClinicA.Id, AppointmentStatus.Confirmed);

        var sut = h.CreateReportService(orgAdmin);
        var csv = await sut.ExportCsvAsync(OrganizationReportTypes.Appointments, new OrganizationReportQuery());
        csv.ContentType.Should().Contain("text/csv");
        csv.FileName.Should().EndWith(".csv");
        var text = Encoding.UTF8.GetString(csv.Content);
        text.Should().Contain("ClinicId");
        text.Should().Contain("TotalAppointments");
        text.ToLowerInvariant().Should().NotContain("password");
        text.ToLowerInvariant().Should().NotContain("token");
        text.ToLowerInvariant().Should().NotContain("medical");
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Override_Organization_Or_Foreign_Clinic()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var orgAdmin = await h.SeedStaffAsync(AppRoles.OrganizationAdmin, h.ClinicA.Id, "oa-deny@test.local");
        var sut = h.CreateReportService(orgAdmin);

        var orgOverride = () => sut.GetStaffAsync(new OrganizationReportQuery { OrganizationId = Guid.NewGuid() });
        await orgOverride.Should().ThrowAsync<OrganizationReportException>()
            .Where(e => e.ErrorCode == OrganizationReportErrorCodes.InvalidScope);

        var clinic = () => sut.GetStaffAsync(new OrganizationReportQuery { ClinicId = Guid.NewGuid() });
        await clinic.Should().ThrowAsync<OrganizationReportException>()
            .Where(e => e.ErrorCode == OrganizationReportErrorCodes.ClinicNotFound);
    }

    [Fact]
    public async Task Clinic_Admin_Is_Denied_Reports()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var clinicAdmin = await h.SeedStaffAsync(AppRoles.ClinicAdmin, h.ClinicA.Id, "ca-rep@test.local");
        var sut = h.CreateReportService(clinicAdmin);
        var act = () => sut.GetPatientsAsync(new OrganizationReportQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Platform_Admin_Requires_Bypass_And_Organization()
    {
        await using var h = await DashboardHarness.CreateAsync();
        var platform = await h.SeedPlatformAdminAsync("plat-rep@test.local");
        var sut = h.CreatePlatformReportService(platform);

        var without = () => sut.GetStaffAsync(new OrganizationReportQuery { OrganizationId = h.Org.Id });
        await without.Should().ThrowAsync<AuthorizationException>();

        var ok = await sut.GetStaffAsync(
            new OrganizationReportQuery { OrganizationId = h.Org.Id },
            PlatformAdminBypass.Explicit);
        ok.Context.OrganizationId.Should().Be(h.Org.Id);
    }

    [Fact]
    public void Organization_Admin_Has_Reports_Permission()
    {
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.OrganizationAdmin)
            .Should().Contain(Permissions.Organizations.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.PlatformAdmin)
            .Should().Contain(Permissions.Organizations.ReportsRead);
        RolePermissionMatrix.GetPermissionsForRole(AppRoles.ClinicAdmin)
            .Should().NotContain(Permissions.Organizations.ReportsRead);
        Permissions.All.Should().Contain(Permissions.Organizations.ReportsRead);
    }
}
