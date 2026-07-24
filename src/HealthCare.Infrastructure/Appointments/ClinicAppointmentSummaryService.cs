using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
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
    private readonly IAuthorizationAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicAppointmentSummaryService> _logger;

    public ClinicAppointmentSummaryService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IClinicAppointmentSummaryBuilder builder,
        IClinicAppointmentSummaryJobs jobs,
        IClinicTimeZoneConverter timeZones,
        IAuthorizationAuditLogger audit,
        TimeProvider timeProvider,
        ILogger<ClinicAppointmentSummaryService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _builder = builder;
        _jobs = jobs;
        _timeZones = timeZones;
        _audit = audit;
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
        var result = await _builder.BuildAsync(clinic.Id, summaryDate, cancellationToken);
        _audit.SummaryOperation(
            "summary_get",
            "succeeded",
            clinic.OrganizationId,
            clinic.Id,
            runId: null);
        return result;
    }

    public async Task<PagedResponse<ClinicAppointmentSummaryRunResponse>> ListRunsForStaffAsync(
        ClinicAppointmentSummaryRunQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveListScopeAsync(query.ClinicId, bypass, cancellationToken);
        IQueryable<ClinicAppointmentSummaryRun> runs = _dbContext.ClinicAppointmentSummaryRuns.AsNoTracking();

        if (scope.Mode == ScopeMode.Clinic || scope.Mode == ScopeMode.Platform)
        {
            runs = runs.Where(r => r.ClinicId == scope.ClinicId);
        }
        else
        {
            runs = runs.Where(r => r.OrganizationId == scope.OrganizationId);
            if (scope.ClinicId.HasValue)
            {
                runs = runs.Where(r => r.ClinicId == scope.ClinicId.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Status)
            && Enum.TryParse<ClinicAppointmentSummaryRunStatus>(query.Status, ignoreCase: true, out var status))
        {
            runs = runs.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.FromDate) && DateOnly.TryParse(query.FromDate, out var fromDate))
        {
            runs = runs.Where(r => r.SummaryDate >= fromDate);
        }

        if (!string.IsNullOrWhiteSpace(query.ToDate) && DateOnly.TryParse(query.ToDate, out var toDate))
        {
            runs = runs.Where(r => r.SummaryDate <= toDate);
        }

        var totalCount = await runs.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1
            ? ClinicAppointmentSummaryRunQueryValidator.DefaultPageSize
            : Math.Min(query.PageSize, ClinicAppointmentSummaryRunQueryValidator.MaxPageSize);

        var rows = await runs
            .OrderByDescending(r => r.SummaryDate)
            .ThenByDescending(r => r.ScheduledAtUtc)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapRun).ToList();

        _audit.SummaryOperation(
            "summary_runs_list",
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId,
            runId: null);

        return PagedResponse<ClinicAppointmentSummaryRunResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<ClinicAppointmentSummaryRunResponse> RetryAsync(
        Guid clinicId,
        DateOnly summaryDate,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var clinic = await ResolveClinicAsync(clinicId, bypass, cancellationToken);

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
        _audit.SummaryOperation(
            "summary_retry",
            "succeeded",
            clinic.OrganizationId,
            clinic.Id,
            run.Id);

        return MapRun(run);
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
            _audit.CrossTenantDenied(
                "summary_patient_denied",
                Contracts.Identity.AuthorizationErrorCodes.Forbidden,
                null,
                null);
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

            _audit.ExplicitPlatformBypassUsed("summary_access", clinic.OrganizationId, clinic.Id);
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
                    _audit.CrossTenantDenied(
                        "summary_clinic_filter_denied",
                        Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                        _currentStaff.OrganizationId,
                        requestedClinicId);
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

    private async Task<ListScope> ResolveListScopeAsync(
        Guid? clinicIdFilter,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.Forbidden();
        }

        if (_currentStaff.HasActiveMembership)
        {
            if (_currentStaff.Role == AppRoles.OrganizationAdmin)
            {
                Guid? clinicId = null;
                if (clinicIdFilter.HasValue)
                {
                    var clinic = await _dbContext.Clinics
                        .AsNoTracking()
                        .SingleOrDefaultAsync(c => c.Id == clinicIdFilter.Value, cancellationToken);
                    if (clinic is null || clinic.OrganizationId != _currentStaff.OrganizationId)
                    {
                        _audit.CrossTenantDenied(
                            "summary_runs_clinic_filter_denied",
                            Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                            _currentStaff.OrganizationId,
                            clinicIdFilter);
                        throw AuthorizationException.ClinicAccessDenied();
                    }

                    clinicId = clinic.Id;
                }

                return ListScope.ForOrganization(_currentStaff.OrganizationId, clinicId);
            }

            return ListScope.ForClinic(_currentStaff.OrganizationId, _currentStaff.ClinicId);
        }

        if (bypass == PlatformAdminBypass.Explicit
            && _currentUser.IsInRole(AppRoles.PlatformAdmin)
            && clinicIdFilter.HasValue)
        {
            var clinic = await _dbContext.Clinics
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == clinicIdFilter.Value, cancellationToken);
            if (clinic is null)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            return ListScope.ForPlatform(clinic.OrganizationId, clinic.Id);
        }

        throw AuthorizationException.MissingStaffMembership();
    }

    private static ClinicAppointmentSummaryRunResponse MapRun(ClinicAppointmentSummaryRun run) =>
        new()
        {
            RunId = run.Id,
            ClinicId = run.ClinicId,
            OrganizationId = run.OrganizationId,
            SummaryDate = run.SummaryDate.ToString("yyyy-MM-dd"),
            Status = run.Status.ToString(),
            AttemptCount = run.AttemptCount,
            AppointmentCount = run.AppointmentCount,
            LastErrorCode = run.LastErrorCode,
            LastError = run.LastError,
            ScheduledAtUtc = run.ScheduledAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            BackgroundJobId = run.BackgroundJobId,
        };

    private enum ScopeMode
    {
        Clinic,
        Organization,
        Platform,
    }

    private sealed class ListScope
    {
        private ListScope(ScopeMode mode, Guid organizationId, Guid? clinicId)
        {
            Mode = mode;
            OrganizationId = organizationId;
            ClinicId = clinicId;
        }

        public ScopeMode Mode { get; }

        public Guid OrganizationId { get; }

        public Guid? ClinicId { get; }

        public static ListScope ForClinic(Guid organizationId, Guid clinicId) =>
            new(ScopeMode.Clinic, organizationId, clinicId);

        public static ListScope ForOrganization(Guid organizationId, Guid? clinicId) =>
            new(ScopeMode.Organization, organizationId, clinicId);

        public static ListScope ForPlatform(Guid organizationId, Guid clinicId) =>
            new(ScopeMode.Platform, organizationId, clinicId);
    }
}
