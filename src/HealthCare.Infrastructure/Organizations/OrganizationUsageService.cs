using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Organizations;

public sealed class OrganizationUsageService : IOrganizationUsageService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IOrganizationLimitService _limits;
    private readonly AuditRetentionOptions _retention;
    private readonly TimeProvider _timeProvider;

    public OrganizationUsageService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IOrganizationLimitService limits,
        IOptions<AuditRetentionOptions> retention,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _limits = limits;
        _retention = retention.Value;
        _timeProvider = timeProvider;
    }

    public async Task<OrganizationUsageResponse> GetUsageAsync(
        OrganizationUsageQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized();
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        var snapshot = await _limits.GetSnapshotAsync(scope.OrganizationId, scope.ClinicId, cancellationToken);

        return new OrganizationUsageResponse
        {
            OrganizationId = snapshot.OrganizationId,
            OrganizationName = snapshot.OrganizationName,
            ClinicId = scope.ClinicId,
            ClinicCount = snapshot.ClinicCount,
            ActiveClinicCount = snapshot.ActiveClinicCount,
            StaffCount = snapshot.StaffCount,
            ActiveStaffCount = snapshot.ActiveStaffCount,
            ActiveDoctorCount = snapshot.ActiveDoctorCount,
            PatientCount = snapshot.PatientCount,
            MonthlyAppointmentCount = snapshot.MonthlyAppointmentCount,
            MaxClinics = snapshot.MaxClinics,
            MaxStaff = snapshot.MaxStaff,
            RemainingClinicCapacity = snapshot.RemainingClinicCapacity,
            RemainingStaffCapacity = snapshot.RemainingStaffCapacity,
            ClinicLimitWarning = snapshot.ClinicLimitWarning,
            StaffLimitWarning = snapshot.StaffLimitWarning,
            ClinicLimitReached = snapshot.ClinicLimitReached,
            StaffLimitReached = snapshot.StaffLimitReached,
            WarningThresholdPercent = snapshot.WarningThresholdPercent,
            AuditRetentionDays = Math.Max(1, _retention.RetentionDays),
            GeneratedAtUtc = _timeProvider.GetUtcNow(),
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
            throw OrganizationUsageException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(Permissions.Organizations.UsageRead);
    }

    private async Task<UsageScope> ResolveScopeAsync(
        OrganizationUsageQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.OrganizationId is null || query.OrganizationId == Guid.Empty)
            {
                throw OrganizationUsageException.OrganizationScopeRequired();
            }

            var orgExists = await _dbContext.Organizations.AsNoTracking()
                .AnyAsync(o => o.Id == query.OrganizationId.Value, cancellationToken);
            if (!orgExists)
            {
                throw OrganizationUsageException.OrganizationNotFound();
            }

            Guid? clinicId = null;
            if (query.ClinicId is Guid requestedClinic && requestedClinic != Guid.Empty)
            {
                var clinicOk = await _dbContext.Clinics.AsNoTracking()
                    .AnyAsync(
                        c => c.Id == requestedClinic && c.OrganizationId == query.OrganizationId.Value,
                        cancellationToken);
                if (!clinicOk)
                {
                    _audit.CrossTenantDenied(
                        "organization_usage_clinic",
                        OrganizationUsageErrorCodes.ClinicNotFound,
                        query.OrganizationId,
                        requestedClinic);
                    throw OrganizationUsageException.ClinicNotFound();
                }

                clinicId = requestedClinic;
            }

            _audit.ExplicitPlatformBypassUsed("organization_usage", query.OrganizationId, clinicId);
            return new UsageScope(query.OrganizationId.Value, clinicId);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw OrganizationUsageException.AccessDenied();
        }

        if (query.OrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "organization_usage_org_override",
                OrganizationUsageErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw OrganizationUsageException.InvalidScope();
        }

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
                    "organization_usage_clinic",
                    OrganizationUsageErrorCodes.ClinicNotFound,
                    _currentStaff.OrganizationId,
                    clinicFilter);
                throw OrganizationUsageException.ClinicNotFound();
            }

            scopedClinicId = clinicFilter;
        }

        return new UsageScope(_currentStaff.OrganizationId, scopedClinicId);
    }

    private sealed record UsageScope(Guid OrganizationId, Guid? ClinicId);
}
