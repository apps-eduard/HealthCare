using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Doctors;
using HealthCare.Contracts.Doctors;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Doctors;

/// <summary>
/// Doctor-scoped operational aggregates. Filters by authenticated DoctorStaffMemberId
/// (or explicit Platform Admin target). Date boundaries use the clinic IANA timezone.
/// Successful reads are not persisted as audit events (same policy as clinic dashboard).
/// </summary>
public sealed class DoctorDashboardService : IDoctorDashboardService
{
    public const int RecentNoShowInclusiveDays = 30;

    private static readonly AppointmentStatus[] UpcomingEligibleStatuses =
    [
        AppointmentStatus.Requested,
        AppointmentStatus.Confirmed,
        AppointmentStatus.CheckedIn,
        AppointmentStatus.InProgress,
    ];

    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DoctorDashboardService> _logger;

    public DoctorDashboardService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<DoctorDashboardService> logger)
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

    public async Task<DoctorDashboardResponse> GetAsync(
        DoctorDashboardQuery query,
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
                throw DoctorDashboardException.InvalidDate();
            }

            explicitDate = parsed;
        }

        var clinic = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == scope.ClinicId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.OrganizationId,
                c.TimeZoneId,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DoctorDashboardException.ClinicNotFound();

        var doctor = await _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.Id == scope.DoctorStaffMemberId)
            .Select(s => new
            {
                s.Id,
                s.DisplayName,
                s.FirstName,
                s.LastName,
                s.ClinicId,
                s.Role,
                s.IsActive,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw DoctorDashboardException.DoctorNotFound();

        if (doctor.ClinicId != clinic.Id || doctor.Role != AppRoles.Doctor)
        {
            throw DoctorDashboardException.DoctorNotFound();
        }

        var organizationName = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == clinic.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        var nowUtc = _timeProvider.GetUtcNow();
        var dashboardDate = explicitDate ?? _timeZones.GetClinicDate(nowUtc, clinic.TimeZoneId);
        var dayStart = _timeZones.ToUtc(dashboardDate, TimeOnly.MinValue, clinic.TimeZoneId);
        var dayEnd = _timeZones.ToUtc(dashboardDate.AddDays(1), TimeOnly.MinValue, clinic.TimeZoneId);
        var noShowFromDate = dashboardDate.AddDays(-(RecentNoShowInclusiveDays - 1));
        var noShowStart = _timeZones.ToUtc(noShowFromDate, TimeOnly.MinValue, clinic.TimeZoneId);
        var noShowEnd = dayEnd;

        var doctorAppointments = _dbContext.Appointments.AsNoTracking()
            .Where(a => a.OrganizationId == clinic.OrganizationId
                && a.ClinicId == clinic.Id
                && a.DoctorStaffMemberId == doctor.Id);

        var todayCount = await doctorAppointments.CountAsync(
            a => a.AppointmentDateUtc >= dayStart && a.AppointmentDateUtc < dayEnd,
            cancellationToken);

        var upcomingCount = await doctorAppointments.CountAsync(
            a => a.AppointmentDateUtc >= nowUtc
                && UpcomingEligibleStatuses.Contains(a.Status),
            cancellationToken);

        var checkedInCount = await doctorAppointments.CountAsync(
            a => a.Status == AppointmentStatus.CheckedIn,
            cancellationToken);

        var awaitingCompletionCount = await doctorAppointments.CountAsync(
            a => a.Status == AppointmentStatus.CheckedIn
                || a.Status == AppointmentStatus.InProgress,
            cancellationToken);

        var recentNoShowCount = await doctorAppointments.CountAsync(
            a => a.Status == AppointmentStatus.NoShow
                && a.AppointmentDateUtc >= noShowStart
                && a.AppointmentDateUtc < noShowEnd,
            cancellationToken);

        var nextRow = await (
            from appointment in doctorAppointments
            join patient in _dbContext.Patients.AsNoTracking()
                on appointment.PatientId equals patient.Id
            where appointment.AppointmentDateUtc >= nowUtc
                && UpcomingEligibleStatuses.Contains(appointment.Status)
            orderby appointment.AppointmentDateUtc
            select new
            {
                appointment.Id,
                appointment.AppointmentDateUtc,
                appointment.Status,
                patient.FirstName,
                patient.LastName,
            }).FirstOrDefaultAsync(cancellationToken);

        DoctorDashboardNextAppointment? next = null;
        if (nextRow is not null)
        {
            var patientName = $"{nextRow.FirstName} {nextRow.LastName}".Trim();
            next = new DoctorDashboardNextAppointment
            {
                AppointmentId = nextRow.Id,
                AppointmentDateUtc = nextRow.AppointmentDateUtc,
                Status = nextRow.Status.ToString(),
                PatientDisplayName = string.IsNullOrWhiteSpace(patientName) ? "Patient" : patientName,
            };
        }

        var warnings = await BuildAvailabilityWarningsAsync(
            clinic.Id,
            doctor.Id,
            dashboardDate,
            cancellationToken);

        var doctorDisplayName = ResolveDoctorDisplayName(doctor.DisplayName, doctor.FirstName, doctor.LastName);

        _logger.LogInformation(
            "Doctor dashboard loaded. DoctorStaffMemberId={DoctorStaffMemberId} ClinicId={ClinicId} Date={Date}",
            doctor.Id,
            clinic.Id,
            dashboardDate);

        return new DoctorDashboardResponse
        {
            DoctorStaffMemberId = doctor.Id,
            DoctorDisplayName = doctorDisplayName,
            ClinicId = clinic.Id,
            ClinicName = clinic.Name,
            OrganizationId = clinic.OrganizationId,
            OrganizationName = organizationName,
            DefaultTimeZoneId = clinic.TimeZoneId,
            LocalDashboardDate = dashboardDate.ToString("yyyy-MM-dd"),
            TimeZoneStrategy = "clinic",
            TodayAppointmentCount = todayCount,
            UpcomingAppointmentCount = upcomingCount,
            CheckedInAppointmentCount = checkedInCount,
            AwaitingCompletionCount = awaitingCompletionCount,
            RecentNoShowCount = recentNoShowCount,
            NextAppointment = next,
            AvailabilityWarningCount = warnings.Count,
            AvailabilityWarnings = warnings,
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
            throw DoctorDashboardException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Doctors.DashboardRead);
    }

    private async Task<DoctorScope> ResolveScopeAsync(
        DoctorDashboardQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.ClinicId is null || query.ClinicId == Guid.Empty)
            {
                throw DoctorDashboardException.ClinicScopeRequired();
            }

            if (query.DoctorStaffMemberId is null || query.DoctorStaffMemberId == Guid.Empty)
            {
                throw DoctorDashboardException.DoctorScopeRequired();
            }

            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(c => c.Id == query.ClinicId.Value, cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "doctor_dashboard",
                    DoctorDashboardErrorCodes.ClinicNotFound,
                    null,
                    query.ClinicId);
                throw DoctorDashboardException.ClinicNotFound();
            }

            var doctorOk = await _dbContext.StaffMembers.AsNoTracking()
                .AnyAsync(
                    s => s.Id == query.DoctorStaffMemberId.Value
                        && s.ClinicId == query.ClinicId.Value
                        && s.Role == AppRoles.Doctor
                        && s.IsActive,
                    cancellationToken);
            if (!doctorOk)
            {
                _audit.CrossTenantDenied(
                    "doctor_dashboard",
                    DoctorDashboardErrorCodes.DoctorNotFound,
                    null,
                    query.ClinicId);
                throw DoctorDashboardException.DoctorNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("doctor_dashboard", null, query.ClinicId);
            return new DoctorScope(query.ClinicId.Value, query.DoctorStaffMemberId.Value);
        }

        if (bypass == PlatformAdminBypass.Explicit && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw DoctorDashboardException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (!string.Equals(_currentStaff.Role, AppRoles.Doctor, StringComparison.Ordinal))
        {
            throw DoctorDashboardException.AccessDenied();
        }

        if (query.ClinicId is Guid requestedClinic
            && requestedClinic != Guid.Empty
            && requestedClinic != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied(
                "doctor_dashboard_clinic_override",
                DoctorDashboardErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                requestedClinic);
            throw DoctorDashboardException.InvalidScope();
        }

        if (query.DoctorStaffMemberId is Guid requestedDoctor
            && requestedDoctor != Guid.Empty
            && requestedDoctor != _currentStaff.StaffMemberId)
        {
            _audit.CrossTenantDenied(
                "doctor_dashboard_doctor_override",
                DoctorDashboardErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                _currentStaff.ClinicId);
            throw DoctorDashboardException.InvalidScope();
        }

        if (_currentStaff.ClinicId == Guid.Empty || _currentStaff.StaffMemberId == Guid.Empty)
        {
            throw DoctorDashboardException.DoctorNotFound();
        }

        return new DoctorScope(_currentStaff.ClinicId, _currentStaff.StaffMemberId);
    }

    private async Task<IReadOnlyList<string>> BuildAvailabilityWarningsAsync(
        Guid clinicId,
        Guid doctorStaffMemberId,
        DateOnly dashboardDate,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var hasWeekly = await _dbContext.DoctorAvailabilities.AsNoTracking()
            .AnyAsync(
                a => a.ClinicId == clinicId
                    && a.DoctorStaffMemberId == doctorStaffMemberId
                    && a.IsActive
                    && a.EffectiveFrom <= dashboardDate
                    && (a.EffectiveTo == null || a.EffectiveTo >= dashboardDate),
                cancellationToken);

        if (!hasWeekly)
        {
            warnings.Add("No weekly availability is configured for the current period.");
        }

        var hasFullDayExceptionToday = await _dbContext.DoctorAvailabilityExceptions.AsNoTracking()
            .AnyAsync(
                e => e.ClinicId == clinicId
                    && e.DoctorStaffMemberId == doctorStaffMemberId
                    && e.Date == dashboardDate
                    && e.ExceptionType == AvailabilityExceptionType.UnavailableFullDay,
                cancellationToken);

        if (hasFullDayExceptionToday)
        {
            warnings.Add("A full-day unavailability exception is set for today.");
        }

        return warnings;
    }

    private static string ResolveDoctorDisplayName(string? displayName, string firstName, string lastName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        var combined = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(combined) ? "Doctor" : combined;
    }

    private sealed record DoctorScope(Guid ClinicId, Guid DoctorStaffMemberId);
}
