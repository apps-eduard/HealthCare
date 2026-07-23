using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Patients;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Patients;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Patients;

/// <summary>
/// Staff patient search and clinic-profile administration.
/// Tenant scope is enforced explicitly on every query (EF global filters remain deferred).
/// </summary>
public sealed class StaffPatientService : IStaffPatientService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly ILogger<StaffPatientService> _logger;

    public StaffPatientService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        ILogger<StaffPatientService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _logger = logger;
    }

    public async Task<PagedResponse<StaffPatientSummaryResponse>> SearchAsync(
        StaffPatientSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(request.ClinicId, bypass, cancellationToken);

        var clinicPatients = ApplyClinicPatientScope(_dbContext.ClinicPatients.AsNoTracking(), scope);

        var query =
            from cp in clinicPatients
            join p in _dbContext.Patients.AsNoTracking() on cp.PatientId equals p.Id
            select new { ClinicPatient = cp, Patient = p };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Patient.FirstName.ToLower().Contains(term)
                || x.Patient.LastName.ToLower().Contains(term)
                || (x.Patient.MiddleName != null && x.Patient.MiddleName.ToLower().Contains(term))
                || x.ClinicPatient.LocalPatientNumber.ToLower().Contains(term)
                || (x.Patient.MobileNumber != null && x.Patient.MobileNumber.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.LocalPatientNumber))
        {
            var local = request.LocalPatientNumber.Trim();
            query = query.Where(x => x.ClinicPatient.LocalPatientNumber == local);
        }

        if (request.PatientIsActive.HasValue)
        {
            query = query.Where(x => x.Patient.IsActive == request.PatientIsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ClinicPatientStatus)
            && Enum.TryParse<ClinicPatientStatus>(request.ClinicPatientStatus, ignoreCase: true, out var cpStatus))
        {
            query = query.Where(x => x.ClinicPatient.Status == cpStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var desc = request.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = request.SortBy.ToLowerInvariant();

        query = sortBy switch
        {
            "localpatientnumber" => desc
                ? query.OrderByDescending(x => x.ClinicPatient.LocalPatientNumber).ThenBy(x => x.ClinicPatient.Id)
                : query.OrderBy(x => x.ClinicPatient.LocalPatientNumber).ThenBy(x => x.ClinicPatient.Id),
            "firstname" => desc
                ? query.OrderByDescending(x => x.Patient.FirstName).ThenBy(x => x.ClinicPatient.Id)
                : query.OrderBy(x => x.Patient.FirstName).ThenBy(x => x.ClinicPatient.Id),
            "lastname" => desc
                ? query.OrderByDescending(x => x.Patient.LastName).ThenBy(x => x.ClinicPatient.Id)
                : query.OrderBy(x => x.Patient.LastName).ThenBy(x => x.ClinicPatient.Id),
            "clinicpatientstatus" => desc
                ? query.OrderByDescending(x => x.ClinicPatient.Status).ThenBy(x => x.ClinicPatient.Id)
                : query.OrderBy(x => x.ClinicPatient.Status).ThenBy(x => x.ClinicPatient.Id),
            "patientisactive" => desc
                ? query.OrderByDescending(x => x.Patient.IsActive).ThenBy(x => x.ClinicPatient.Id)
                : query.OrderBy(x => x.Patient.IsActive).ThenBy(x => x.ClinicPatient.Id),
            _ => desc
                ? query.OrderByDescending(x => x.ClinicPatient.RegisteredAtUtc).ThenBy(x => x.ClinicPatient.Id)
                : query.OrderBy(x => x.ClinicPatient.RegisteredAtUtc).ThenBy(x => x.ClinicPatient.Id),
        };

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? StaffPatientSearchRequestValidator.DefaultPageSize
            : Math.Min(request.PageSize, StaffPatientSearchRequestValidator.MaxPageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StaffPatientSummaryResponse
            {
                PatientId = x.Patient.Id,
                ClinicPatientId = x.ClinicPatient.Id,
                ClinicId = x.ClinicPatient.ClinicId,
                LocalPatientNumber = x.ClinicPatient.LocalPatientNumber,
                FirstName = x.Patient.FirstName,
                MiddleName = x.Patient.MiddleName,
                LastName = x.Patient.LastName,
                DateOfBirth = x.Patient.DateOfBirth,
                Gender = x.Patient.Gender,
                MobileNumber = x.Patient.MobileNumber,
                PreferredLanguage = x.Patient.PreferredLanguage,
                PatientIsActive = x.Patient.IsActive,
                ClinicPatientStatus = x.ClinicPatient.Status.ToString(),
                RegisteredAtUtc = x.ClinicPatient.RegisteredAtUtc,
                Version = x.ClinicPatient.Version,
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Staff patient search. UserId={UserId} StaffMemberId={StaffMemberId} ClinicId={ClinicId} OrganizationId={OrganizationId} ResultCount={ResultCount} Operation={Operation}",
            _currentUser.UserId,
            scope.StaffMemberId,
            scope.ClinicId,
            scope.OrganizationId,
            totalCount,
            "staff_patient_search");

        return PagedResponse<StaffPatientSummaryResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<StaffPatientDetailResponse> GetByPatientIdAsync(
        Guid patientId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(clinicIdFilter: null, bypass, cancellationToken);
        var enrollment = await FindEnrollmentInScopeAsync(patientId, scope, asNoTracking: true, cancellationToken);

        if (enrollment is null)
        {
            LogCrossTenantDenied(patientId, scope, "staff_patient_detail_denied");
            throw AuthorizationException.PatientSelfScopeDenied();
        }

        _logger.LogInformation(
            "Staff patient detail accessed. UserId={UserId} StaffMemberId={StaffMemberId} ClinicId={ClinicId} PatientId={PatientId} Operation={Operation}",
            _currentUser.UserId,
            scope.StaffMemberId,
            enrollment.ClinicPatient.ClinicId,
            patientId,
            "staff_patient_detail");

        return MapDetail(enrollment.ClinicPatient, enrollment.Patient);
    }

    public async Task<StaffPatientDetailResponse> UpdateClinicProfileAsync(
        Guid patientId,
        UpdateClinicPatientRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveScopeAsync(clinicIdFilter: null, bypass, cancellationToken);
        var enrollment = await FindEnrollmentInScopeAsync(patientId, scope, asNoTracking: false, cancellationToken);

        if (enrollment is null)
        {
            LogCrossTenantDenied(patientId, scope, "staff_clinic_patient_update_denied");
            throw AuthorizationException.PatientSelfScopeDenied();
        }

        var clinicPatient = enrollment.ClinicPatient;

        if (clinicPatient.Version != request.ExpectedVersion)
        {
            _logger.LogInformation(
                "Clinic patient concurrency conflict. UserId={UserId} StaffMemberId={StaffMemberId} ClinicPatientId={ClinicPatientId} ExpectedVersion={ExpectedVersion} ActualVersion={ActualVersion}",
                _currentUser.UserId,
                scope.StaffMemberId,
                clinicPatient.Id,
                request.ExpectedVersion,
                clinicPatient.Version);
            throw new ClinicPatientConcurrencyException();
        }

        if (!Enum.TryParse<ClinicPatientStatus>(request.Status, ignoreCase: true, out var newStatus))
        {
            throw AuthorizationException.Forbidden();
        }

        clinicPatient.Status = newStatus;
        clinicPatient.Version++;
        clinicPatient.UpdatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogInformation(
                "Clinic patient concurrency conflict. UserId={UserId} ClinicPatientId={ClinicPatientId}",
                _currentUser.UserId,
                clinicPatient.Id);
            throw new ClinicPatientConcurrencyException();
        }

        _logger.LogInformation(
            "Clinic patient status updated. UserId={UserId} StaffMemberId={StaffMemberId} ClinicId={ClinicId} PatientId={PatientId} Status={Status} Operation={Operation}",
            _currentUser.UserId,
            scope.StaffMemberId,
            clinicPatient.ClinicId,
            patientId,
            clinicPatient.Status,
            "staff_clinic_patient_update");

        return MapDetail(clinicPatient, enrollment.Patient);
    }

    private async Task<StaffScope> ResolveScopeAsync(
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
                        _logger.LogInformation(
                            "Cross-tenant access denied. UserId={UserId} StaffMemberId={StaffMemberId} OrganizationId={OrganizationId} RequestedClinicId={ClinicId} Operation={Operation}",
                            _currentUser.UserId,
                            _currentStaff.StaffMemberId,
                            _currentStaff.OrganizationId,
                            clinicIdFilter,
                            "staff_patient_clinic_filter_denied");
                        throw AuthorizationException.ClinicAccessDenied();
                    }

                    clinicId = clinic.Id;
                }

                return StaffScope.ForOrganization(
                    _currentStaff.StaffMemberId,
                    _currentStaff.OrganizationId,
                    clinicId);
            }

            // Clinic-scoped roles: always use trusted ClinicId; ignore client ClinicId.
            if (clinicIdFilter.HasValue && clinicIdFilter.Value != _currentStaff.ClinicId)
            {
                _logger.LogInformation(
                    "Client ClinicId ignored. UserId={UserId} StaffMemberId={StaffMemberId} TrustedClinicId={ClinicId} ClientClinicId={ClientClinicId}",
                    _currentUser.UserId,
                    _currentStaff.StaffMemberId,
                    _currentStaff.ClinicId,
                    clinicIdFilter);
            }

            return StaffScope.ForClinic(
                _currentStaff.StaffMemberId,
                _currentStaff.OrganizationId,
                _currentStaff.ClinicId);
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (!clinicIdFilter.HasValue)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            var clinic = await _dbContext.Clinics
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == clinicIdFilter.Value, cancellationToken);

            if (clinic is null)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            _logger.LogInformation(
                "PLATFORM_ADMIN explicit staff patient bypass. UserId={UserId} ClinicId={ClinicId}",
                _currentUser.UserId,
                clinic.Id);

            return StaffScope.ForPlatformBypass(clinic.OrganizationId, clinic.Id);
        }

        throw AuthorizationException.MissingStaffMembership();
    }

    private IQueryable<ClinicPatient> ApplyClinicPatientScope(IQueryable<ClinicPatient> query, StaffScope scope)
    {
        if (scope.Mode == ScopeMode.Clinic || scope.Mode == ScopeMode.PlatformBypass)
        {
            return query.Where(cp => cp.ClinicId == scope.ClinicId!.Value);
        }

        // Organization: only clinics in the trusted organization.
        var organizationId = scope.OrganizationId;
        query =
            from cp in query
            join c in _dbContext.Clinics.AsNoTracking() on cp.ClinicId equals c.Id
            where c.OrganizationId == organizationId
            select cp;

        if (scope.ClinicId.HasValue)
        {
            var clinicId = scope.ClinicId.Value;
            query = query.Where(cp => cp.ClinicId == clinicId);
        }

        return query;
    }

    private async Task<TrackedEnrollment?> FindEnrollmentInScopeAsync(
        Guid patientId,
        StaffScope scope,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        IQueryable<ClinicPatient> cpQuery = _dbContext.ClinicPatients;
        if (asNoTracking)
        {
            cpQuery = cpQuery.AsNoTracking();
        }

        cpQuery = ApplyClinicPatientScope(cpQuery, scope);

        var query =
            from cp in cpQuery
            join p in (asNoTracking ? _dbContext.Patients.AsNoTracking() : _dbContext.Patients)
                on cp.PatientId equals p.Id
            where cp.PatientId == patientId
            select new { cp, p };

        if (scope.Mode == ScopeMode.Organization
            && !scope.ClinicId.HasValue
            && _currentStaff.HasActiveMembership)
        {
            var preferredClinicId = _currentStaff.ClinicId;
            query = query
                .OrderByDescending(x => x.cp.ClinicId == preferredClinicId)
                .ThenBy(x => x.cp.RegisteredAtUtc);
        }

        var row = await query.FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new TrackedEnrollment(row.cp, row.p);
    }

    private void LogCrossTenantDenied(Guid patientId, StaffScope scope, string operation)
    {
        _logger.LogInformation(
            "Cross-tenant access denied. UserId={UserId} StaffMemberId={StaffMemberId} ClinicId={ClinicId} OrganizationId={OrganizationId} PatientId={PatientId} Operation={Operation}",
            _currentUser.UserId,
            scope.StaffMemberId,
            scope.ClinicId,
            scope.OrganizationId,
            patientId,
            operation);
    }

    private static StaffPatientDetailResponse MapDetail(ClinicPatient cp, Patient p) =>
        new()
        {
            PatientId = p.Id,
            ClinicPatientId = cp.Id,
            ClinicId = cp.ClinicId,
            LocalPatientNumber = cp.LocalPatientNumber,
            FirstName = p.FirstName,
            MiddleName = p.MiddleName,
            LastName = p.LastName,
            DateOfBirth = p.DateOfBirth,
            Gender = p.Gender,
            MobileNumber = p.MobileNumber,
            PreferredLanguage = p.PreferredLanguage,
            PatientIsActive = p.IsActive,
            ClinicPatientStatus = cp.Status.ToString(),
            RegisteredAtUtc = cp.RegisteredAtUtc,
            Version = cp.Version,
            Address = p.Address,
            EmergencyContact = p.EmergencyContact,
        };

    private sealed record TrackedEnrollment(ClinicPatient ClinicPatient, Patient Patient);

    private enum ScopeMode
    {
        Clinic,
        Organization,
        PlatformBypass,
    }

    private sealed class StaffScope
    {
        private StaffScope(
            ScopeMode mode,
            Guid? staffMemberId,
            Guid organizationId,
            Guid? clinicId)
        {
            Mode = mode;
            StaffMemberId = staffMemberId;
            OrganizationId = organizationId;
            ClinicId = clinicId;
        }

        public ScopeMode Mode { get; }

        public Guid? StaffMemberId { get; }

        public Guid OrganizationId { get; }

        public Guid? ClinicId { get; }

        public static StaffScope ForClinic(Guid staffMemberId, Guid organizationId, Guid clinicId) =>
            new(ScopeMode.Clinic, staffMemberId, organizationId, clinicId);

        public static StaffScope ForOrganization(Guid staffMemberId, Guid organizationId, Guid? clinicId) =>
            new(ScopeMode.Organization, staffMemberId, organizationId, clinicId);

        public static StaffScope ForPlatformBypass(Guid organizationId, Guid clinicId) =>
            new(ScopeMode.PlatformBypass, null, organizationId, clinicId);
    }
}
