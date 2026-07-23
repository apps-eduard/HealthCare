using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class ClinicAppointmentSummaryService : IClinicAppointmentSummaryService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IClinicAppointmentSummaryBuilder _builder;
    private readonly IClinicAppointmentSummaryJobs _jobs;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicAppointmentSummaryService> _logger;

    public ClinicAppointmentSummaryService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IClinicAppointmentSummaryBuilder builder,
        IClinicAppointmentSummaryJobs jobs,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<ClinicAppointmentSummaryService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _builder = builder;
        _jobs = jobs;
        _timeZones = timeZones;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ClinicAppointmentSummaryResponse> GetForStaffAsync(
        ClinicAppointmentSummaryQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var clinic = await ResolveClinicAsync(query.ClinicId, bypass, cancellationToken);
        var summaryDate = ResolveDate(query.Date, clinic.TimeZoneId);

        return await _builder.BuildAsync(clinic.Id, summaryDate, cancellationToken);
    }

    public async Task<ClinicAppointmentSummaryRunResponse> RetryAsync(
        Guid clinicId,
        DateOnly summaryDate,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        await ResolveClinicAsync(clinicId, bypass, cancellationToken);

        var key = ClinicAppointmentSummaryRun.BuildIdempotencyKey(clinicId, summaryDate);
        var run = await _dbContext.ClinicAppointmentSummaryRuns
            .SingleOrDefaultAsync(r => r.IdempotencyKey == key, cancellationToken)
            ?? throw AppointmentSummaryException.NotFound();

        if (run.Status == ClinicAppointmentSummaryRunStatus.Completed)
        {
            throw AppointmentSummaryException.AlreadyCompleted();
        }

        if (run.Status is not (ClinicAppointmentSummaryRunStatus.Failed or ClinicAppointmentSummaryRunStatus.Pending))
        {
            throw AppointmentSummaryException.NotRetryable();
        }

        if (run.AttemptCount >= ClinicAppointmentSummaryRun.MaxAttempts
            && run.Status == ClinicAppointmentSummaryRunStatus.Failed)
        {
            run.AttemptCount = Math.Max(0, ClinicAppointmentSummaryRun.MaxAttempts - 1);
        }

        run.Status = ClinicAppointmentSummaryRunStatus.Pending;
        run.LastError = null;
        run.LastErrorCode = null;
        run.ScheduledAtUtc = _timeProvider.GetUtcNow();

        var jobId = _jobs.EnqueueProcess(run.Id);
        run.BackgroundJobId = jobId;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Summary retried. UserId={UserId} ClinicId={ClinicId} SummaryDate={SummaryDate} RunId={RunId}",
            _currentUser.UserId,
            clinicId,
            summaryDate,
            run.Id);

        return new ClinicAppointmentSummaryRunResponse
        {
            RunId = run.Id,
            ClinicId = run.ClinicId,
            SummaryDate = run.SummaryDate.ToString("yyyy-MM-dd"),
            Status = run.Status.ToString(),
            AttemptCount = run.AttemptCount,
            AppointmentCount = run.AppointmentCount,
            LastErrorCode = run.LastErrorCode,
        };
    }

    private DateOnly ResolveDate(string? date, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return _timeZones.GetClinicDate(_timeProvider.GetUtcNow(), timeZoneId);
        }

        if (!DateOnly.TryParse(date, out var parsed))
        {
            throw AppointmentSummaryException.InvalidDate();
        }

        return parsed;
    }

    private async Task<Domain.Clinics.Clinic> ResolveClinicAsync(
        Guid? requestedClinicId,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            _logger.LogInformation(
                "Cross-tenant summary access denied. UserId={UserId} Reason=patient",
                _currentUser.UserId);
            throw AuthorizationException.Forbidden();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (!requestedClinicId.HasValue)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            var clinic = await _dbContext.Clinics
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == requestedClinicId.Value, cancellationToken)
                ?? throw AppointmentSummaryException.NotFound();

            _logger.LogInformation(
                "PLATFORM_ADMIN explicit summary bypass. UserId={UserId} ClinicId={ClinicId}",
                _currentUser.UserId,
                clinic.Id);
            return clinic;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            Guid clinicId;
            if (requestedClinicId.HasValue)
            {
                var clinic = await _dbContext.Clinics
                    .AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == requestedClinicId.Value, cancellationToken);
                if (clinic is null || clinic.OrganizationId != _currentStaff.OrganizationId)
                {
                    _logger.LogInformation(
                        "Cross-tenant summary access denied. UserId={UserId} Reason=org_clinic",
                        _currentUser.UserId);
                    throw AppointmentSummaryException.NotFound();
                }

                clinicId = clinic.Id;
            }
            else
            {
                clinicId = _currentStaff.ClinicId;
            }

            return await _dbContext.Clinics.AsNoTracking().SingleAsync(c => c.Id == clinicId, cancellationToken);
        }

        if (requestedClinicId.HasValue && requestedClinicId.Value != _currentStaff.ClinicId)
        {
            _logger.LogInformation(
                "Client ClinicId ignored for summary. UserId={UserId} TrustedClinicId={ClinicId}",
                _currentUser.UserId,
                _currentStaff.ClinicId);
        }

        return await _dbContext.Clinics
            .AsNoTracking()
            .SingleAsync(c => c.Id == _currentStaff.ClinicId, cancellationToken);
    }
}
