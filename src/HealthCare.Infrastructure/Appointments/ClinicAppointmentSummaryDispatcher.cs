using HealthCare.Application.Appointments;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class ClinicAppointmentSummaryDispatcher : IClinicAppointmentSummaryDispatcher
{
    private readonly HealthCareDbContext _dbContext;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly IClinicAppointmentSummaryJobs _jobs;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicAppointmentSummaryDispatcher> _logger;

    public ClinicAppointmentSummaryDispatcher(
        HealthCareDbContext dbContext,
        IClinicTimeZoneConverter timeZones,
        IClinicAppointmentSummaryJobs jobs,
        TimeProvider timeProvider,
        ILogger<ClinicAppointmentSummaryDispatcher> logger)
    {
        _dbContext = dbContext;
        _timeZones = timeZones;
        _jobs = jobs;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var clinics = await _dbContext.Clinics
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Join(
                _dbContext.Organizations.AsNoTracking().Where(o => o.Status == OrganizationStatus.Active),
                c => c.OrganizationId,
                o => o.Id,
                (c, _) => c)
            .Select(c => new { c.Id, c.OrganizationId, c.TimeZoneId, c.Slug })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Summary dispatch evaluated. ClinicCandidateCount={ClinicCandidateCount}",
            clinics.Count);

        foreach (var clinic in clinics)
        {
            DateOnly localDate;
            TimeOnly localTime;
            try
            {
                localDate = _timeZones.GetClinicDate(nowUtc, clinic.TimeZoneId);
                localTime = _timeZones.GetClinicTime(nowUtc, clinic.TimeZoneId);
            }
            catch (Exception)
            {
                _logger.LogInformation(
                    "Summary dispatch skipped invalid timezone. ClinicId={ClinicId} TimeZoneId={TimeZoneId}",
                    clinic.Id,
                    clinic.TimeZoneId);
                continue;
            }

            if (localTime < ClinicAppointmentSummaryRun.DefaultLocalSendTime)
            {
                continue;
            }

            var key = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinic.Id, localDate);
            var existing = await _dbContext.ClinicAppointmentSummaryRuns
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken);

            if (existing is not null)
            {
                if (existing.Status == ClinicAppointmentSummaryRunStatus.Completed)
                {
                    _logger.LogInformation(
                        "Summary skipped as duplicate. ClinicId={ClinicId} SummaryDate={SummaryDate} RunId={RunId}",
                        clinic.Id,
                        localDate,
                        existing.Id);
                    continue;
                }

                if (existing.Status is ClinicAppointmentSummaryRunStatus.Pending
                    or ClinicAppointmentSummaryRunStatus.Processing)
                {
                    continue;
                }

                // Failed runs are recovered separately.
                continue;
            }

            var scheduledAtUtc = _timeZones.ToUtc(
                localDate,
                ClinicAppointmentSummaryRun.DefaultLocalSendTime,
                clinic.TimeZoneId);

            var run = new ClinicAppointmentSummaryRun
            {
                Id = Guid.NewGuid(),
                ClinicId = clinic.Id,
                OrganizationId = clinic.OrganizationId,
                SummaryDate = localDate,
                ScheduledAtUtc = scheduledAtUtc,
                Status = ClinicAppointmentSummaryRunStatus.Pending,
                AttemptCount = 0,
                IdempotencyKey = key,
            };

            _dbContext.ClinicAppointmentSummaryRuns.Add(run);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(run).State = EntityState.Detached;
                _logger.LogInformation(
                    "Summary skipped as duplicate (concurrent). ClinicId={ClinicId} SummaryDate={SummaryDate}",
                    clinic.Id,
                    localDate);
                continue;
            }

            var jobId = _jobs.EnqueueProcess(run.Id);
            run.BackgroundJobId = jobId;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Clinic summary queued. ClinicId={ClinicId} OrganizationId={OrganizationId} SummaryDate={SummaryDate} RunId={RunId}",
                clinic.Id,
                clinic.OrganizationId,
                localDate,
                run.Id);
        }
    }
}
