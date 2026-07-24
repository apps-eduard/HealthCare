using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Organizations;

/// <summary>
/// Organization-scoped operational aggregates for the Organization Admin dashboard.
/// Appointment "today" uses Option A: each clinic's local calendar date independently when no
/// explicit date is supplied and multiple clinics are in scope.
/// </summary>
public sealed class OrganizationDashboardService : IOrganizationDashboardService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrganizationDashboardService> _logger;

    public OrganizationDashboardService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<OrganizationDashboardService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _timeZones = timeZones;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OrganizationDashboardResponse> GetAsync(
        OrganizationDashboardQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);

        DateOnly? explicitDate = null;
        if (!string.IsNullOrWhiteSpace(query.Date))
        {
            if (!DateOnly.TryParse(query.Date, out var parsed))
            {
                throw OrganizationDashboardException.InvalidDate();
            }

            explicitDate = parsed;
        }

        var clinics = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.OrganizationId == scope.OrganizationId)
            .Where(c => scope.ClinicId == null || c.Id == scope.ClinicId.Value)
            .Select(c => new ClinicRow(c.Id, c.Name, c.IsActive, c.TimeZoneId))
            .ToListAsync(cancellationToken);

        if (scope.ClinicId is Guid requiredClinic
            && clinics.All(c => c.Id != requiredClinic))
        {
            throw OrganizationDashboardException.ClinicNotFound();
        }

        var clinicIds = clinics.Select(c => c.Id).ToList();

        var organization = new OrganizationDashboardOrganizationSummary
        {
            OrganizationId = scope.OrganizationId,
            OrganizationName = scope.OrganizationName,
            ActiveClinicCount = clinics.Count(c => c.IsActive),
            InactiveClinicCount = clinics.Count(c => !c.IsActive),
            TotalClinicCount = clinics.Count,
        };

        var staff = await LoadStaffSummaryAsync(scope.OrganizationId, clinicIds, cancellationToken);
        var patients = await LoadPatientSummaryAsync(clinicIds, cancellationToken);
        var appointments = await LoadAppointmentSummaryAsync(clinics, explicitDate, cancellationToken);
        var alerts = await LoadAlertsAsync(scope.OrganizationId, clinicIds, cancellationToken);

        ClinicRow? selected = null;
        if (scope.ClinicId is Guid selectedId)
        {
            selected = clinics.SingleOrDefault(c => c.Id == selectedId);
        }

        string? dashboardDate;
        string? timeZoneId;
        string strategy;
        if (selected is not null)
        {
            strategy = "clinic";
            timeZoneId = selected.TimeZoneId;
            dashboardDate = (explicitDate
                ?? _timeZones.GetClinicDate(_timeProvider.GetUtcNow(), selected.TimeZoneId))
                .ToString("yyyy-MM-dd");
        }
        else
        {
            strategy = "per_clinic_local";
            timeZoneId = null;
            dashboardDate = explicitDate?.ToString("yyyy-MM-dd");
        }

        _logger.LogInformation(
            "Organization dashboard loaded. ActorUserId={ActorUserId} OrganizationId={OrganizationId} ClinicId={ClinicId} ClinicCount={ClinicCount}",
            _currentUser.UserId,
            scope.OrganizationId,
            scope.ClinicId,
            clinics.Count);

        return new OrganizationDashboardResponse
        {
            Organization = organization,
            Staff = staff,
            Patients = patients,
            Appointments = appointments,
            Alerts = alerts,
            Context = new OrganizationDashboardContext
            {
                SelectedClinicId = selected?.Id,
                SelectedClinicName = selected?.Name,
                TimeZoneId = timeZoneId,
                DashboardDate = dashboardDate,
                TimeZoneStrategy = strategy,
            },
        };
    }

    private void EnsureAuthorized()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw OrganizationDashboardException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Organizations.DashboardRead);
    }

    private async Task<DashboardScope> ResolveScopeAsync(
        OrganizationDashboardQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.OrganizationId is null || query.OrganizationId == Guid.Empty)
            {
                throw OrganizationDashboardException.OrganizationScopeRequired();
            }

            var org = await _dbContext.Organizations.AsNoTracking()
                .Where(o => o.Id == query.OrganizationId.Value)
                .Select(o => new { o.Id, o.Name })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw OrganizationDashboardException.OrganizationNotFound();

            Guid? clinicId = null;
            if (query.ClinicId is Guid requestedClinic && requestedClinic != Guid.Empty)
            {
                var clinicOk = await _dbContext.Clinics.AsNoTracking()
                    .AnyAsync(
                        c => c.Id == requestedClinic && c.OrganizationId == org.Id,
                        cancellationToken);
                if (!clinicOk)
                {
                    _audit.CrossTenantDenied(
                        "organization_dashboard_clinic",
                        OrganizationDashboardErrorCodes.ClinicNotFound,
                        org.Id,
                        requestedClinic);
                    throw OrganizationDashboardException.ClinicNotFound();
                }

                clinicId = requestedClinic;
            }

            _audit.ExplicitPlatformBypassUsed("organization_dashboard", org.Id, clinicId);
            return new DashboardScope(org.Id, org.Name, clinicId);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw OrganizationDashboardException.AccessDenied();
        }

        // Never trust client OrganizationId for ORGANIZATION_ADMIN.
        if (query.OrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "organization_dashboard_org_override",
                OrganizationDashboardErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw OrganizationDashboardException.InvalidScope();
        }

        var organizationName = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == _currentStaff.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw OrganizationDashboardException.OrganizationNotFound();

        Guid? scopedClinicId = null;
        if (query.ClinicId is Guid clinicFilter && clinicFilter != Guid.Empty)
        {
            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(
                    c => c.Id == clinicFilter && c.OrganizationId == _currentStaff.OrganizationId,
                    cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "organization_dashboard_clinic",
                    OrganizationDashboardErrorCodes.ClinicNotFound,
                    _currentStaff.OrganizationId,
                    clinicFilter);
                throw OrganizationDashboardException.ClinicNotFound();
            }

            scopedClinicId = clinicFilter;
        }

        return new DashboardScope(_currentStaff.OrganizationId, organizationName, scopedClinicId);
    }

    private async Task<OrganizationDashboardStaffSummary> LoadStaffSummaryAsync(
        Guid organizationId,
        IReadOnlyList<Guid> clinicIds,
        CancellationToken cancellationToken)
    {
        if (clinicIds.Count == 0)
        {
            return new OrganizationDashboardStaffSummary();
        }

        var rows = await _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && clinicIds.Contains(s.ClinicId))
            .GroupBy(s => new { s.IsActive, s.Role })
            .Select(g => new { g.Key.IsActive, g.Key.Role, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var active = rows.Where(r => r.IsActive).Sum(r => r.Count);
        var inactive = rows.Where(r => !r.IsActive).Sum(r => r.Count);

        int RoleCount(string role) =>
            rows.Where(r => r.IsActive && string.Equals(r.Role, role, StringComparison.Ordinal))
                .Sum(r => r.Count);

        return new OrganizationDashboardStaffSummary
        {
            ActiveStaffCount = active,
            InactiveStaffCount = inactive,
            DoctorCount = RoleCount(AppRoles.Doctor),
            NurseCount = RoleCount(AppRoles.Nurse),
            ReceptionistCount = RoleCount(AppRoles.Receptionist),
            ClinicAdminCount = RoleCount(AppRoles.ClinicAdmin),
        };
    }

    private async Task<OrganizationDashboardPatientSummary> LoadPatientSummaryAsync(
        IReadOnlyList<Guid> clinicIds,
        CancellationToken cancellationToken)
    {
        if (clinicIds.Count == 0)
        {
            return new OrganizationDashboardPatientSummary();
        }

        // Distinct patients enrolled in scoped clinics; active if any enrollment is Active.
        var enrollments = await _dbContext.ClinicPatients.AsNoTracking()
            .Where(cp => clinicIds.Contains(cp.ClinicId))
            .Select(cp => new { cp.PatientId, cp.Status })
            .ToListAsync(cancellationToken);

        var byPatient = enrollments
            .GroupBy(x => x.PatientId)
            .Select(g => g.Any(x => x.Status == ClinicPatientStatus.Active))
            .ToList();

        var active = byPatient.Count(x => x);
        var inactive = byPatient.Count(x => !x);

        return new OrganizationDashboardPatientSummary
        {
            TotalPatientCount = active + inactive,
            ActivePatientCount = active,
            InactivePatientCount = inactive,
        };
    }

    private async Task<OrganizationDashboardAppointmentSummary> LoadAppointmentSummaryAsync(
        IReadOnlyList<ClinicRow> clinics,
        DateOnly? explicitDate,
        CancellationToken cancellationToken)
    {
        if (clinics.Count == 0)
        {
            return new OrganizationDashboardAppointmentSummary();
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var ranges = clinics
            .GroupBy(c => c.TimeZoneId, StringComparer.Ordinal)
            .Select(g =>
            {
                var date = explicitDate ?? _timeZones.GetClinicDate(nowUtc, g.Key);
                var start = _timeZones.ToUtc(date, TimeOnly.MinValue, g.Key);
                var end = _timeZones.ToUtc(date.AddDays(1), TimeOnly.MinValue, g.Key);
                return new
                {
                    ClinicIds = g.Select(c => c.Id).ToList(),
                    Start = start,
                    End = end,
                };
            })
            .ToList();

        var totals = new Dictionary<AppointmentStatus, int>();
        foreach (var status in Enum.GetValues<AppointmentStatus>())
        {
            totals[status] = 0;
        }

        foreach (var range in ranges)
        {
            var rows = await _dbContext.Appointments.AsNoTracking()
                .Where(a => range.ClinicIds.Contains(a.ClinicId)
                    && a.AppointmentDateUtc >= range.Start
                    && a.AppointmentDateUtc < range.End)
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                totals[row.Status] = totals.GetValueOrDefault(row.Status) + row.Count;
            }
        }

        var cancelled = totals.GetValueOrDefault(AppointmentStatus.CancelledByPatient)
            + totals.GetValueOrDefault(AppointmentStatus.CancelledByClinic);
        var total = totals.Values.Sum();

        return new OrganizationDashboardAppointmentSummary
        {
            TotalAppointments = total,
            RequestedCount = totals.GetValueOrDefault(AppointmentStatus.Requested),
            ConfirmedCount = totals.GetValueOrDefault(AppointmentStatus.Confirmed),
            CheckedInCount = totals.GetValueOrDefault(AppointmentStatus.CheckedIn),
            InProgressCount = totals.GetValueOrDefault(AppointmentStatus.InProgress),
            CompletedCount = totals.GetValueOrDefault(AppointmentStatus.Completed),
            CancelledCount = cancelled,
            NoShowCount = totals.GetValueOrDefault(AppointmentStatus.NoShow),
        };
    }

    private async Task<OrganizationDashboardAlerts> LoadAlertsAsync(
        Guid organizationId,
        IReadOnlyList<Guid> clinicIds,
        CancellationToken cancellationToken)
    {
        if (clinicIds.Count == 0)
        {
            return new OrganizationDashboardAlerts();
        }

        var failedReminders = await (
            from reminder in _dbContext.AppointmentReminders.AsNoTracking()
            join appointment in _dbContext.Appointments.AsNoTracking()
                on reminder.AppointmentId equals appointment.Id
            where reminder.Status == AppointmentReminderStatus.Failed
                && appointment.OrganizationId == organizationId
                && clinicIds.Contains(appointment.ClinicId)
            select reminder.Id).CountAsync(cancellationToken);

        var failedSummaries = await _dbContext.ClinicAppointmentSummaryRuns.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId
                && clinicIds.Contains(r.ClinicId)
                && r.Status == ClinicAppointmentSummaryRunStatus.Failed)
            .CountAsync(cancellationToken);

        var clinicsWithActiveDoctor = await _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                && clinicIds.Contains(s.ClinicId)
                && s.IsActive
                && s.Role == AppRoles.Doctor)
            .Select(s => s.ClinicId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var clinicsWithoutDoctor = clinicIds.Count - clinicsWithActiveDoctor.Count;

        var clinicsWithAvailability = await _dbContext.DoctorAvailabilities.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId
                && clinicIds.Contains(a.ClinicId)
                && a.IsActive)
            .Select(a => a.ClinicId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Clinics that have at least one active doctor but no active availability windows.
        var withoutAvailability = clinicsWithActiveDoctor
            .Count(id => !clinicsWithAvailability.Contains(id));

        return new OrganizationDashboardAlerts
        {
            FailedReminderCount = failedReminders,
            FailedClinicSummaryCount = failedSummaries,
            ClinicsWithoutActiveDoctorCount = Math.Max(0, clinicsWithoutDoctor),
            ClinicsWithoutAvailabilityCount = withoutAvailability,
        };
    }

    private sealed record ClinicRow(Guid Id, string Name, bool IsActive, string TimeZoneId);

    private sealed record DashboardScope(Guid OrganizationId, string OrganizationName, Guid? ClinicId);
}
