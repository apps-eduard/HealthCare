using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Organizations;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthCare.Infrastructure.Organizations;

public sealed class OrganizationSettingsService : IOrganizationSettingsService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IOrganizationLimitService _limits;
    private readonly TimeProvider _timeProvider;

    public OrganizationSettingsService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        IOrganizationLimitService limits,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _limits = limits;
        _timeProvider = timeProvider;
    }

    public async Task<OrganizationSettingsResponse> GetAsync(
        OrganizationSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Organizations.ProfileRead);
        var organizationId = await ResolveOrganizationIdAsync(query, bypass, cancellationToken);
        return await MapAsync(organizationId, cancellationToken);
    }

    public async Task<OrganizationSettingsResponse> UpdateAsync(
        UpdateOrganizationSettingsRequest request,
        OrganizationSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Organizations.ProfileUpdate);

        if (!request.HasAnyEditableField)
        {
            throw OrganizationSettingsException.EmptyUpdate();
        }

        var organizationId = await ResolveOrganizationIdAsync(query, bypass, cancellationToken);
        var organization = await _dbContext.Organizations
            .SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw OrganizationSettingsException.OrganizationNotFound();

        if (organization.Version != request.ExpectedVersion)
        {
            throw OrganizationSettingsException.ConcurrencyConflict();
        }

        if (request.NameSpecified)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw OrganizationSettingsException.EmptyUpdate();
            }

            organization.Name = request.Name.Trim();
        }

        if (request.ContactEmailSpecified)
        {
            organization.ContactEmail = NormalizeOptional(request.ContactEmail);
        }

        if (request.ContactPhoneSpecified)
        {
            organization.ContactPhone = NormalizeOptional(request.ContactPhone);
        }

        if (request.CountrySpecified)
        {
            organization.Country = NormalizeOptional(request.Country);
        }

        if (request.DefaultTimeZoneIdSpecified)
        {
            if (string.IsNullOrWhiteSpace(request.DefaultTimeZoneId))
            {
                organization.DefaultTimeZoneId = null;
            }
            else
            {
                EnsureValidTimezone(request.DefaultTimeZoneId);
                organization.DefaultTimeZoneId = request.DefaultTimeZoneId.Trim();
            }
        }

        if (request.BrandingPlaceholderSpecified)
        {
            organization.BrandingPlaceholder = NormalizeOptional(request.BrandingPlaceholder);
        }

        organization.Version++;
        organization.UpdatedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw OrganizationSettingsException.ConcurrencyConflict();
        }

        _audit.OrganizationOperation(
            "organization_profile_update",
            "succeeded",
            organization.Id);

        return await MapAsync(organization.Id, cancellationToken);
    }

    private async Task<OrganizationSettingsResponse> MapAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await _dbContext.Organizations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == organizationId, cancellationToken)
            ?? throw OrganizationSettingsException.OrganizationNotFound();

        var snapshot = await _limits.GetSnapshotAsync(organizationId, clinicId: null, cancellationToken);

        return new OrganizationSettingsResponse
        {
            OrganizationId = organization.Id,
            Name = organization.Name,
            Slug = organization.Slug,
            Status = organization.Status.ToString(),
            ContactEmail = organization.ContactEmail,
            ContactPhone = organization.ContactPhone,
            Country = organization.Country,
            DefaultTimeZoneId = organization.DefaultTimeZoneId,
            BrandingPlaceholder = organization.BrandingPlaceholder,
            Version = organization.Version,
            CreatedAtUtc = organization.CreatedAtUtc,
            UpdatedAtUtc = organization.UpdatedAtUtc,
            MaxClinics = snapshot.MaxClinics,
            MaxStaff = snapshot.MaxStaff,
            ClinicCount = snapshot.ClinicCount,
            StaffCount = snapshot.StaffCount,
            RemainingClinicCapacity = snapshot.RemainingClinicCapacity,
            RemainingStaffCapacity = snapshot.RemainingStaffCapacity,
        };
    }

    private void EnsureAuthorized(string permission)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw OrganizationSettingsException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(permission);
    }

    private async Task<Guid> ResolveOrganizationIdAsync(
        OrganizationSettingsQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.OrganizationId is null || query.OrganizationId == Guid.Empty)
            {
                throw OrganizationSettingsException.OrganizationScopeRequired();
            }

            var exists = await _dbContext.Organizations.AsNoTracking()
                .AnyAsync(o => o.Id == query.OrganizationId.Value, cancellationToken);
            if (!exists)
            {
                throw OrganizationSettingsException.OrganizationNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("organization_settings", query.OrganizationId, null);
            return query.OrganizationId.Value;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw OrganizationSettingsException.AccessDenied();
        }

        if (query.OrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "organization_settings_org_override",
                OrganizationSettingsErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw OrganizationSettingsException.InvalidScope();
        }

        return _currentStaff.OrganizationId;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static void EnsureValidTimezone(string timeZoneId)
    {
        var id = timeZoneId.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return;
        }
        catch (TimeZoneNotFoundException)
        {
            if (string.Equals(id, "Asia/Riyadh", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _ = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
                    return;
                }
                catch (TimeZoneNotFoundException)
                {
                    // fall through
                }
            }
        }
        catch (InvalidTimeZoneException)
        {
            // fall through
        }

        throw OrganizationSettingsException.InvalidTimezone();
    }
}
