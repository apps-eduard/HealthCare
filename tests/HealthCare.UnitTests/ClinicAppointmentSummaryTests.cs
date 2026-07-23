using FluentAssertions;
using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class ClinicAppointmentSummaryTests
{
    [Fact]
    public async Task Active_Clinic_Summary_Generated()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        await h.SeedAppointmentsAsync(data);

        var summary = await h.CreateBuilder().BuildAsync(data.ClinicAId, data.SummaryDate);
        summary.TotalAppointments.Should().Be(3);
        summary.Requested.Should().Be(1);
        summary.Confirmed.Should().Be(1);
        summary.CancelledByPatient.Should().Be(1);
        summary.ByDoctor.Should().ContainSingle(g => g.Count == 3);
        summary.Appointments.Should().HaveCount(3);
        summary.Appointments.Should().OnlyContain(a =>
            !string.IsNullOrWhiteSpace(a.Status)
            && !string.IsNullOrWhiteSpace(a.DoctorDisplayName));
        typeof(ClinicAppointmentSummaryItem).GetProperty("Reason").Should().BeNull();
        typeof(ClinicAppointmentSummaryItem).GetProperty("PatientId").Should().BeNull();
    }

    [Fact]
    public async Task Inactive_Clinic_Skipped_By_Dispatcher()
    {
        await using var h = await SummaryHarness.CreateAsync(localHour: 7);
        var data = await h.SeedAsync();
        var clinic = await h.Db.Clinics.SingleAsync(c => c.Id == data.ClinicAId);
        clinic.IsActive = false;
        await h.Db.SaveChangesAsync();

        await h.CreateDispatcher().DispatchDueAsync();
        (await h.Db.ClinicAppointmentSummaryRuns.CountAsync(r => r.ClinicId == data.ClinicAId)).Should().Be(0);
    }

    [Fact]
    public async Task Inactive_Organization_Skipped_By_Dispatcher()
    {
        await using var h = await SummaryHarness.CreateAsync(localHour: 7);
        var data = await h.SeedAsync();
        var org = await h.Db.Organizations.SingleAsync(o => o.Id == data.Org1Id);
        org.Status = OrganizationStatus.Inactive;
        await h.Db.SaveChangesAsync();

        await h.CreateDispatcher().DispatchDueAsync();
        (await h.Db.ClinicAppointmentSummaryRuns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Correct_Clinic_Local_Date_Boundaries_Asia_Riyadh()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        // 2026-07-24 21:00 UTC = 2026-07-25 00:00 Riyadh — belongs to Jul 25, not Jul 24.
        h.Db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            PatientId = data.PatientId,
            ClinicPatientId = data.ClinicPatientId,
            DoctorStaffMemberId = data.DoctorAStaffId,
            AppointmentDateUtc = DateTimeOffset.Parse("2026-07-24T21:00:00Z"),
            DurationMinutes = 30,
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.Staff,
            CreatedByUserId = data.DoctorAUserId,
            Version = 0,
        });
        await h.Db.SaveChangesAsync();

        var jul24 = await h.CreateBuilder().BuildAsync(data.ClinicAId, new DateOnly(2026, 7, 24));
        var jul25 = await h.CreateBuilder().BuildAsync(data.ClinicAId, new DateOnly(2026, 7, 25));
        jul24.TotalAppointments.Should().Be(0);
        jul25.TotalAppointments.Should().Be(1);
        jul25.FirstAppointmentLocal.Should().StartWith("2026-07-25");
    }

    [Fact]
    public void Asia_Riyadh_Timezone_Conversion()
    {
        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        var utc = converter.ToUtc(new DateOnly(2026, 7, 24), new TimeOnly(6, 0), "Asia/Riyadh");
        utc.Should().Be(DateTimeOffset.Parse("2026-07-24T03:00:00Z"));
        converter.GetClinicDate(utc, "Asia/Riyadh").Should().Be(new DateOnly(2026, 7, 24));
    }

    [Fact]
    public void Dst_Capable_Timezone_Conversion()
    {
        var converter = new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance);
        // Eastern Standard Time observes DST on Windows hosts.
        var winter = converter.ToUtc(new DateOnly(2026, 1, 15), new TimeOnly(6, 0), "Eastern Standard Time");
        var summer = converter.ToUtc(new DateOnly(2026, 7, 15), new TimeOnly(6, 0), "Eastern Standard Time");
        winter.Should().Be(DateTimeOffset.Parse("2026-01-15T11:00:00Z")); // EST UTC-5
        summer.Should().Be(DateTimeOffset.Parse("2026-07-15T10:00:00Z")); // EDT UTC-4
    }

    [Fact]
    public async Task No_Cross_Clinic_Appointments_Included()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        await h.SeedAppointmentsAsync(data);
        await h.SeedClinicBAppointmentAsync(data);

        var summary = await h.CreateBuilder().BuildAsync(data.ClinicAId, data.SummaryDate);
        summary.TotalAppointments.Should().Be(3);
        summary.ClinicId.Should().Be(data.ClinicAId);
    }

    [Fact]
    public async Task Cancelled_Appointments_Counted()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        await h.SeedAppointmentsAsync(data);
        var summary = await h.CreateBuilder().BuildAsync(data.ClinicAId, data.SummaryDate);
        summary.CancelledByPatient.Should().Be(1);
        summary.TotalAppointments.Should().Be(3);
    }

    [Fact]
    public async Task Duplicate_Summary_Run_Prevented_And_Completed_Not_Resent()
    {
        await using var h = await SummaryHarness.CreateAsync(localHour: 7);
        var data = await h.SeedAsync();
        await h.SeedAppointmentsAsync(data);

        await h.CreateDispatcher().DispatchDueAsync();
        await h.CreateDispatcher().DispatchDueAsync();
        (await h.Db.ClinicAppointmentSummaryRuns.CountAsync(r => r.ClinicId == data.ClinicAId)).Should().Be(1);

        var run = await h.Db.ClinicAppointmentSummaryRuns.SingleAsync(r => r.ClinicId == data.ClinicAId);
        var sender = new CountingSender();
        await h.CreateProcessor(sender).ProcessRunAsync(run.Id);
        sender.Sent.Should().Be(1);

        await h.CreateProcessor(sender).ProcessRunAsync(run.Id);
        sender.Sent.Should().Be(1);
        (await h.Db.ClinicAppointmentSummaryRuns.SingleAsync(r => r.Id == run.Id))
            .Status.Should().Be(ClinicAppointmentSummaryRunStatus.Completed);
    }

    [Fact]
    public async Task Concurrent_Dispatcher_Does_Not_Enqueue_Duplicates()
    {
        await using var h = await SummaryHarness.CreateAsync(localHour: 7);
        var data = await h.SeedAsync();

        // Simulate race winner already inserted a Pending run for the same key.
        var key = ClinicAppointmentSummaryRun.BuildIdempotencyKey(data.ClinicAId, data.SummaryDate);
        h.Db.ClinicAppointmentSummaryRuns.Add(new ClinicAppointmentSummaryRun
        {
            Id = Guid.NewGuid(),
            ClinicId = data.ClinicAId,
            OrganizationId = data.Org1Id,
            SummaryDate = data.SummaryDate,
            ScheduledAtUtc = h.Time.GetUtcNow(),
            Status = ClinicAppointmentSummaryRunStatus.Pending,
            IdempotencyKey = key,
        });
        await h.Db.SaveChangesAsync();

        await h.CreateDispatcher().DispatchDueAsync();
        (await h.Db.ClinicAppointmentSummaryRuns.CountAsync(r => r.ClinicId == data.ClinicAId)).Should().Be(1);
        var clinicARunIds = await h.Db.ClinicAppointmentSummaryRuns
            .Where(r => r.ClinicId == data.ClinicAId)
            .Select(r => r.Id)
            .ToListAsync();
        h.Jobs.Enqueued.Where(id => clinicARunIds.Contains(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sender_Failure_Updates_Safe_Failure_State_And_Can_Retry()
    {
        await using var h = await SummaryHarness.CreateAsync(localHour: 7);
        var data = await h.SeedAsync();
        await h.SeedAppointmentsAsync(data);
        await h.CreateDispatcher().DispatchDueAsync();
        var run = await h.Db.ClinicAppointmentSummaryRuns.SingleAsync(r => r.ClinicId == data.ClinicAId);

        var failing = new FailingSender();
        var act = () => h.CreateProcessor(failing).ProcessRunAsync(run.Id);
        await act.Should().ThrowAsync<AppointmentSummaryException>()
            .Where(e => e.ErrorCode == AppointmentSummaryErrorCodes.SummaryDeliveryFailed);

        run = await h.Db.ClinicAppointmentSummaryRuns.SingleAsync(r => r.Id == run.Id);
        run.Status.Should().Be(ClinicAppointmentSummaryRunStatus.Failed);
        run.LastErrorCode.Should().Be(AppointmentSummaryErrorCodes.SummaryDeliveryFailed);
        run.LastError.Should().Be("delivery_failed");

        h.Jobs.Enqueued.Clear();
        await h.CreateRecovery().RecoverAsync();
        h.Jobs.Enqueued.Should().Contain(run.Id);

        var sender = new CountingSender();
        await h.CreateProcessor(sender).ProcessRunAsync(run.Id);
        sender.Sent.Should().Be(1);
        (await h.Db.ClinicAppointmentSummaryRuns.SingleAsync(r => r.Id == run.Id))
            .Status.Should().Be(ClinicAppointmentSummaryRunStatus.Completed);
    }

    [Fact]
    public async Task Recovery_Ignores_Completed_Runs()
    {
        await using var h = await SummaryHarness.CreateAsync(localHour: 7);
        var data = await h.SeedAsync();
        var clinicB = await h.Db.Clinics.SingleAsync(c => c.Id == data.ClinicBId);
        clinicB.IsActive = false;
        await h.Db.SaveChangesAsync();

        await h.CreateDispatcher().DispatchDueAsync();
        var run = await h.Db.ClinicAppointmentSummaryRuns.SingleAsync(r => r.ClinicId == data.ClinicAId);
        await h.CreateProcessor().ProcessRunAsync(run.Id);

        h.Jobs.Enqueued.Clear();
        await h.CreateRecovery().RecoverAsync();
        h.Jobs.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Patient_Denied_From_Staff_Summary_Endpoint_Service()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateStaffService(data.PatientUserId, data.Org1Id, data.ClinicAId, Guid.Empty, AppRoles.Patient, isPatient: true);
        var act = () => sut.GetForStaffAsync(new ClinicAppointmentSummaryQuery());
        await act.Should().ThrowAsync<AuthorizationException>();
    }

    [Fact]
    public async Task Cross_Clinic_Staff_Uses_Trusted_Clinic_Only()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        var sut = h.CreateStaffService(data.DoctorBUserId, data.Org1Id, data.ClinicBId, data.DoctorBStaffId, AppRoles.Doctor);
        // Clinic staff ignore foreign ClinicId and use trusted clinic.
        var summary = await sut.GetForStaffAsync(new ClinicAppointmentSummaryQuery { ClinicId = data.ClinicAId });
        summary.ClinicId.Should().Be(data.ClinicBId);
    }

    [Fact]
    public async Task Organization_Admin_Remains_Organization_Scoped()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = await h.SeedOrgAdminAsync(data);
        var sut = h.CreateStaffService(admin.UserId, data.Org1Id, data.ClinicAId, admin.StaffId, AppRoles.OrganizationAdmin);

        var summary = await sut.GetForStaffAsync(new ClinicAppointmentSummaryQuery { ClinicId = data.ClinicBId });
        summary.ClinicId.Should().Be(data.ClinicBId);

        var org2 = Guid.NewGuid();
        var clinic2 = Guid.NewGuid();
        h.Db.Organizations.Add(new Organization { Id = org2, Name = "O2", Slug = "o2", Status = OrganizationStatus.Active });
        h.Db.Clinics.Add(new Domain.Clinics.Clinic
        {
            Id = clinic2,
            OrganizationId = org2,
            Name = "X",
            Slug = "x",
            TimeZoneId = "Asia/Riyadh",
            IsActive = true,
        });
        await h.Db.SaveChangesAsync();

        var act = () => sut.GetForStaffAsync(new ClinicAppointmentSummaryQuery { ClinicId = clinic2 });
        await act.Should().ThrowAsync<AppointmentSummaryException>()
            .Where(e => e.ErrorCode == AppointmentSummaryErrorCodes.SummaryNotFound);
    }

    [Fact]
    public async Task Explicit_Platform_Admin_Bypass()
    {
        await using var h = await SummaryHarness.CreateAsync();
        var data = await h.SeedAsync();
        var admin = h.CreatePlatformAdminService();

        var without = () => admin.GetForStaffAsync(new ClinicAppointmentSummaryQuery { ClinicId = data.ClinicAId });
        await without.Should().ThrowAsync<AuthorizationException>();

        var summary = await admin.GetForStaffAsync(
            new ClinicAppointmentSummaryQuery { ClinicId = data.ClinicAId },
            PlatformAdminBypass.Explicit);
        summary.ClinicId.Should().Be(data.ClinicAId);
    }

    [Fact]
    public async Task Dispatcher_Before_Six_Am_Does_Not_Enqueue()
    {
        await using var h = await SummaryHarness.CreateAsync(localHour: 5);
        var data = await h.SeedAsync();
        await h.CreateDispatcher().DispatchDueAsync();
        (await h.Db.ClinicAppointmentSummaryRuns.CountAsync(r => r.ClinicId == data.ClinicAId)).Should().Be(0);
    }

    [Fact]
    public void Summary_Dto_Has_No_Sensitive_Fields()
    {
        typeof(ClinicAppointmentSummaryResponse).GetProperty("Reason").Should().BeNull();
        typeof(ClinicAppointmentSummaryResponse).GetProperty("PatientNotes").Should().BeNull();
        typeof(ClinicAppointmentSummaryItem).GetProperty("PatientId").Should().BeNull();
        typeof(ClinicAppointmentSummaryItem).GetProperty("LocalPatientNumber").Should().BeNull();
        typeof(ClinicAppointmentSummaryItem).GetProperty("DateOfBirth").Should().BeNull();
    }

    private sealed class CountingSender : IClinicAppointmentSummarySender
    {
        public int Sent { get; private set; }

        public Task SendAsync(ClinicAppointmentSummaryResponse summary, CancellationToken cancellationToken = default)
        {
            Sent++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSender : IClinicAppointmentSummarySender
    {
        public Task SendAsync(ClinicAppointmentSummaryResponse summary, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated_delivery_failure");
    }
}

internal sealed class SummaryHarness : IAsyncDisposable
{
    private SummaryHarness(HealthCare.Infrastructure.Persistence.HealthCareDbContext db, TimeProvider time)
    {
        Db = db;
        Time = time;
        Jobs = new ImmediateClinicAppointmentSummaryJobs();
    }

    public HealthCare.Infrastructure.Persistence.HealthCareDbContext Db { get; }

    public TimeProvider Time { get; }

    public ImmediateClinicAppointmentSummaryJobs Jobs { get; }

    public static async Task<SummaryHarness> CreateAsync(int localHour = 12)
    {
        var options = new DbContextOptionsBuilder<HealthCare.Infrastructure.Persistence.HealthCareDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new HealthCare.Infrastructure.Persistence.HealthCareDbContext(options);
        await db.Database.EnsureCreatedAsync();
        // Asia/Riyadh UTC+3 → localHour maps to UTC = localHour - 3
        var utc = DateTimeOffset.Parse($"2026-07-24T{localHour - 3:00}:00:00Z");
        return new SummaryHarness(db, new FixedTimeProvider(utc));
    }

    public async Task<SeedData> SeedAsync()
    {
        var org1 = Guid.NewGuid();
        var clinicA = Guid.NewGuid();
        var clinicB = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var patientUserId = Guid.NewGuid();
        var doctorAUser = Guid.NewGuid();
        var doctorBUser = Guid.NewGuid();
        var doctorAStaff = Guid.NewGuid();
        var doctorBStaff = Guid.NewGuid();
        var clinicPatientId = Guid.NewGuid();
        var summaryDate = new DateOnly(2026, 7, 24);

        Db.Organizations.Add(new Organization
        {
            Id = org1,
            Name = "Org",
            Slug = "org-summary",
            Status = OrganizationStatus.Active,
        });
        Db.Clinics.AddRange(
            new Domain.Clinics.Clinic
            {
                Id = clinicA,
                OrganizationId = org1,
                Name = "A",
                Slug = "summary-a",
                TimeZoneId = "Asia/Riyadh",
                IsActive = true,
            },
            new Domain.Clinics.Clinic
            {
                Id = clinicB,
                OrganizationId = org1,
                Name = "B",
                Slug = "summary-b",
                TimeZoneId = "Asia/Riyadh",
                IsActive = true,
            });
        Db.Patients.Add(new Domain.Patients.Patient
        {
            Id = patientId,
            UserId = patientUserId,
            FirstName = "Pat",
            LastName = "Ent",
            IsActive = true,
        });
        Db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = clinicPatientId,
            ClinicId = clinicA,
            PatientId = patientId,
            LocalPatientNumber = "A-1",
            Status = Domain.Patients.ClinicPatientStatus.Active,
        });
        Db.StaffMembers.AddRange(
            new Domain.Staff.StaffMember
            {
                Id = doctorAStaff,
                UserId = doctorAUser,
                OrganizationId = org1,
                ClinicId = clinicA,
                Role = AppRoles.Doctor,
                JobTitle = "Dr A",
                IsActive = true,
            },
            new Domain.Staff.StaffMember
            {
                Id = doctorBStaff,
                UserId = doctorBUser,
                OrganizationId = org1,
                ClinicId = clinicB,
                Role = AppRoles.Doctor,
                JobTitle = "Dr B",
                IsActive = true,
            });
        await Db.SaveChangesAsync();

        return new SeedData(
            org1, clinicA, clinicB, patientId, patientUserId, clinicPatientId,
            doctorAUser, doctorBUser, doctorAStaff, doctorBStaff, summaryDate);
    }

    public async Task SeedAppointmentsAsync(SeedData data)
    {
        // 09:00, 10:00, 11:00 Riyadh = 06:00, 07:00, 08:00 UTC on 2026-07-24
        Db.Appointments.AddRange(
            new Appointment
            {
                Id = Guid.NewGuid(),
                OrganizationId = data.Org1Id,
                ClinicId = data.ClinicAId,
                PatientId = data.PatientId,
                ClinicPatientId = data.ClinicPatientId,
                DoctorStaffMemberId = data.DoctorAStaffId,
                AppointmentDateUtc = DateTimeOffset.Parse("2026-07-24T06:00:00Z"),
                DurationMinutes = 30,
                Status = AppointmentStatus.Requested,
                Source = AppointmentSource.Patient,
                CreatedByUserId = data.PatientUserId,
                Version = 0,
            },
            new Appointment
            {
                Id = Guid.NewGuid(),
                OrganizationId = data.Org1Id,
                ClinicId = data.ClinicAId,
                PatientId = data.PatientId,
                ClinicPatientId = data.ClinicPatientId,
                DoctorStaffMemberId = data.DoctorAStaffId,
                AppointmentDateUtc = DateTimeOffset.Parse("2026-07-24T07:00:00Z"),
                DurationMinutes = 30,
                Status = AppointmentStatus.Confirmed,
                Source = AppointmentSource.Staff,
                CreatedByUserId = data.DoctorAUserId,
                Version = 0,
            },
            new Appointment
            {
                Id = Guid.NewGuid(),
                OrganizationId = data.Org1Id,
                ClinicId = data.ClinicAId,
                PatientId = data.PatientId,
                ClinicPatientId = data.ClinicPatientId,
                DoctorStaffMemberId = data.DoctorAStaffId,
                AppointmentDateUtc = DateTimeOffset.Parse("2026-07-24T08:00:00Z"),
                DurationMinutes = 30,
                Status = AppointmentStatus.CancelledByPatient,
                Source = AppointmentSource.Patient,
                CreatedByUserId = data.PatientUserId,
                Version = 0,
            });
        await Db.SaveChangesAsync();
    }

    public async Task SeedClinicBAppointmentAsync(SeedData data)
    {
        var enrollment = Guid.NewGuid();
        Db.ClinicPatients.Add(new Domain.Patients.ClinicPatient
        {
            Id = enrollment,
            ClinicId = data.ClinicBId,
            PatientId = data.PatientId,
            LocalPatientNumber = "B-1",
            Status = Domain.Patients.ClinicPatientStatus.Active,
        });
        Db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicBId,
            PatientId = data.PatientId,
            ClinicPatientId = enrollment,
            DoctorStaffMemberId = data.DoctorBStaffId,
            AppointmentDateUtc = DateTimeOffset.Parse("2026-07-24T06:30:00Z"),
            DurationMinutes = 30,
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.Staff,
            CreatedByUserId = data.DoctorBUserId,
            Version = 0,
        });
        await Db.SaveChangesAsync();
    }

    public async Task<(Guid UserId, Guid StaffId)> SeedOrgAdminAsync(SeedData data)
    {
        var userId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        Db.StaffMembers.Add(new Domain.Staff.StaffMember
        {
            Id = staffId,
            UserId = userId,
            OrganizationId = data.Org1Id,
            ClinicId = data.ClinicAId,
            Role = AppRoles.OrganizationAdmin,
            IsActive = true,
        });
        await Db.SaveChangesAsync();
        return (userId, staffId);
    }

    public ClinicAppointmentSummaryBuilder CreateBuilder() =>
        new(Db, new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance),
            NullLogger<ClinicAppointmentSummaryBuilder>.Instance);

    public ClinicAppointmentSummaryDispatcher CreateDispatcher() =>
        new(Db, new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance), Jobs, Time,
            NullLogger<ClinicAppointmentSummaryDispatcher>.Instance);

    public ClinicAppointmentSummaryProcessor CreateProcessor(IClinicAppointmentSummarySender? sender = null) =>
        new(Db, CreateBuilder(), sender ?? new DevelopmentClinicAppointmentSummarySender(
                NullLogger<DevelopmentClinicAppointmentSummarySender>.Instance), Time,
            NullLogger<ClinicAppointmentSummaryProcessor>.Instance);

    public ClinicAppointmentSummaryRecoveryService CreateRecovery() =>
        new(Db, Jobs, Time, NullLogger<ClinicAppointmentSummaryRecoveryService>.Instance);

    public ClinicAppointmentSummaryService CreateStaffService(
        Guid userId,
        Guid orgId,
        Guid clinicId,
        Guid staffMemberId,
        string role,
        bool isPatient = false)
    {
        var roles = isPatient ? new[] { AppRoles.Patient } : new[] { role };
        var user = new FakeCurrentUser { IsAuthenticated = true, UserId = userId, Roles = roles };
        var staff = isPatient
            ? new FakeCurrentStaff()
            : new FakeCurrentStaff
            {
                HasActiveMembership = true,
                StaffMemberId = staffMemberId,
                OrganizationId = orgId,
                ClinicId = clinicId,
                Role = role,
            };
        return new ClinicAppointmentSummaryService(
            Db, user, staff, CreateBuilder(), Jobs,
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance), Time,
            NullLogger<ClinicAppointmentSummaryService>.Instance);
    }

    public ClinicAppointmentSummaryService CreatePlatformAdminService()
    {
        var user = new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [AppRoles.PlatformAdmin],
        };
        return new ClinicAppointmentSummaryService(
            Db, user, new FakeCurrentStaff(), CreateBuilder(), Jobs,
            new ClinicTimeZoneConverter(NullLogger<ClinicTimeZoneConverter>.Instance), Time,
            NullLogger<ClinicAppointmentSummaryService>.Instance);
    }

    public ValueTask DisposeAsync()
    {
        Db.Dispose();
        return ValueTask.CompletedTask;
    }

    public sealed record SeedData(
        Guid Org1Id,
        Guid ClinicAId,
        Guid ClinicBId,
        Guid PatientId,
        Guid PatientUserId,
        Guid ClinicPatientId,
        Guid DoctorAUserId,
        Guid DoctorBUserId,
        Guid DoctorAStaffId,
        Guid DoctorBStaffId,
        DateOnly SummaryDate);
}
