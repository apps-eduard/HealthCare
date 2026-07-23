using HealthCare.Application.Appointments;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class ClinicAppointmentSummaryProcessor : IClinicAppointmentSummaryProcessor
{
    private readonly HealthCareDbContext _dbContext;
    private readonly IClinicAppointmentSummaryBuilder _builder;
    private readonly IClinicAppointmentSummarySender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicAppointmentSummaryProcessor> _logger;

    public ClinicAppointmentSummaryProcessor(
        HealthCareDbContext dbContext,
        IClinicAppointmentSummaryBuilder builder,
        IClinicAppointmentSummarySender sender,
        TimeProvider timeProvider,
        ILogger<ClinicAppointmentSummaryProcessor> logger)
    {
        _dbContext = dbContext;
        _builder = builder;
        _sender = sender;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _dbContext.ClinicAppointmentSummaryRuns
            .SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run is null)
        {
            _logger.LogInformation("Summary run missing. RunId={RunId}", runId);
            return;
        }

        if (run.Status == ClinicAppointmentSummaryRunStatus.Completed)
        {
            _logger.LogInformation(
                "Summary skipped as duplicate. ClinicId={ClinicId} SummaryDate={SummaryDate} RunId={RunId}",
                run.ClinicId,
                run.SummaryDate,
                run.Id);
            return;
        }

        var clinic = await _dbContext.Clinics
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == run.ClinicId, cancellationToken);
        var organization = clinic is null
            ? null
            : await _dbContext.Organizations
                .AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == clinic.OrganizationId, cancellationToken);

        if (clinic is null || !clinic.IsActive
            || organization is null || organization.Status != OrganizationStatus.Active)
        {
            run.Status = ClinicAppointmentSummaryRunStatus.Failed;
            run.LastErrorCode = AppointmentSummaryErrorCodes.SummaryNotFound;
            run.LastError = "Clinic or organization inactive.";
            run.AttemptCount++;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        run.Status = ClinicAppointmentSummaryRunStatus.Processing;
        run.StartedAtUtc ??= _timeProvider.GetUtcNow();
        run.AttemptCount++;
        run.LastError = null;
        run.LastErrorCode = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        ClinicAppointmentSummaryResponse summary;
        try
        {
            summary = await _builder.BuildAsync(run.ClinicId, run.SummaryDate, cancellationToken);
        }
        catch (Exception)
        {
            await MarkFailedAsync(run, AppointmentSummaryErrorCodes.SummaryGenerationFailed, "generation_failed", cancellationToken);
            throw AppointmentSummaryException.GenerationFailed();
        }

        run.AppointmentCount = summary.TotalAppointments;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _sender.SendAsync(summary, cancellationToken);
        }
        catch (Exception)
        {
            await MarkFailedAsync(run, AppointmentSummaryErrorCodes.SummaryDeliveryFailed, "delivery_failed", cancellationToken);
            _logger.LogInformation(
                "Summary failed. ClinicId={ClinicId} OrganizationId={OrganizationId} SummaryDate={SummaryDate} RunId={RunId} ErrorCode={ErrorCode}",
                run.ClinicId,
                run.OrganizationId,
                run.SummaryDate,
                run.Id,
                AppointmentSummaryErrorCodes.SummaryDeliveryFailed);
            throw AppointmentSummaryException.DeliveryFailed();
        }

        run.Status = ClinicAppointmentSummaryRunStatus.Completed;
        run.CompletedAtUtc = _timeProvider.GetUtcNow();
        run.LastError = null;
        run.LastErrorCode = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Summary delivered. ClinicId={ClinicId} OrganizationId={OrganizationId} SummaryDate={SummaryDate} RunId={RunId} AppointmentCount={AppointmentCount}",
            run.ClinicId,
            run.OrganizationId,
            run.SummaryDate,
            run.Id,
            run.AppointmentCount);
    }

    private async Task MarkFailedAsync(
        ClinicAppointmentSummaryRun run,
        string errorCode,
        string sanitizedMessage,
        CancellationToken cancellationToken)
    {
        run.Status = ClinicAppointmentSummaryRunStatus.Failed;
        run.LastErrorCode = errorCode;
        run.LastError = sanitizedMessage;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
