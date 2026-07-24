using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Contracts.Clinics;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Clinics;

/// <summary>
/// Clinic-scoped profile settings for CLINIC_ADMIN (membership clinic) and PLATFORM_ADMIN (explicit bypass).
/// Reuses the same field/timezone/concurrency rules as organization clinic management without slug/activation.
/// </summary>
public sealed class ClinicSettingsService : IClinicSettingsService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicSettingsService> _logger;

    public ClinicSettingsService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        TimeProvider timeProvider,
        ILogger<ClinicSettingsService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ClinicSettingsResponse> GetAsync(
        ClinicSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.ProfileRead);
        var clinicId = await ResolveClinicIdAsync(query, bypass, cancellationToken);
        return await MapAsync(clinicId, cancellationToken);
    }

    public async Task<ClinicSettingsResponse> UpdateAsync(
        UpdateClinicSettingsRequest request,
        ClinicSettingsQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.ProfileUpdate);

        if (!request.HasAnyEditableField)
        {
            throw ClinicSettingsException.EmptyUpdate();
        }

        var clinicId = await ResolveClinicIdAsync(query, bypass, cancellationToken);
        var clinic = await _dbContext.Clinics
            .SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken)
            ?? throw ClinicSettingsException.ClinicNotFound();

        if (clinic.Version != request.ExpectedVersion)
        {
            throw ClinicSettingsException.ConcurrencyConflict();
        }

        var changed = new List<string>();

        if (request.NameSpecified)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw ClinicSettingsException.EmptyUpdate();
            }

            clinic.Name = request.Name.Trim();
            changed.Add(nameof(UpdateClinicSettingsRequest.Name));
        }

        if (request.SpecialtySpecified)
        {
            clinic.Specialty = NormalizeOptional(request.Specialty);
            changed.Add(nameof(UpdateClinicSettingsRequest.Specialty));
        }

        if (request.ContactEmailSpecified)
        {
            clinic.Email = NormalizeOptional(request.ContactEmail);
            changed.Add(nameof(UpdateClinicSettingsRequest.ContactEmail));
        }

        if (request.ContactPhoneSpecified)
        {
            clinic.PhoneNumber = NormalizeOptional(request.ContactPhone);
            changed.Add(nameof(UpdateClinicSettingsRequest.ContactPhone));
        }

        if (request.AddressSpecified)
        {
            clinic.AddressLine1 = NormalizeOptional(request.Address);
            clinic.Address = clinic.AddressLine1;
            changed.Add(nameof(UpdateClinicSettingsRequest.Address));
        }

        if (request.CitySpecified)
        {
            clinic.City = NormalizeOptional(request.City);
            changed.Add(nameof(UpdateClinicSettingsRequest.City));
        }

        if (request.CountrySpecified)
        {
            clinic.Country = NormalizeOptional(request.Country);
            changed.Add(nameof(UpdateClinicSettingsRequest.Country));
        }

        if (request.DefaultTimeZoneIdSpecified)
        {
            if (string.IsNullOrWhiteSpace(request.DefaultTimeZoneId))
            {
                throw ClinicSettingsException.InvalidTimezone();
            }

            EnsureValidTimezone(request.DefaultTimeZoneId);
            clinic.TimeZoneId = request.DefaultTimeZoneId.Trim();
            changed.Add(nameof(UpdateClinicSettingsRequest.DefaultTimeZoneId));
        }

        if (changed.Count == 0)
        {
            throw ClinicSettingsException.EmptyUpdate();
        }

        clinic.Version++;
        clinic.UpdatedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ClinicSettingsException.ConcurrencyConflict();
        }

        _audit.ClinicOperation(
            "clinic_profile_update",
            "succeeded",
            clinic.OrganizationId,
            clinic.Id,
            changed);

        _logger.LogInformation(
            "Clinic profile updated. ActorUserId={ActorUserId} ClinicId={ClinicId} ChangedFields={ChangedFields}",
            _currentUser.UserId,
            clinic.Id,
            string.Join(',', changed));

        return await MapAsync(clinic.Id, cancellationToken);
    }

    private async Task<ClinicSettingsResponse> MapAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => new
            {
                c.Id,
                c.OrganizationId,
                c.Name,
                c.Slug,
                c.Specialty,
                c.Email,
                c.PhoneNumber,
                c.AddressLine1,
                c.Address,
                c.City,
                c.Country,
                c.TimeZoneId,
                c.IsActive,
                c.CreatedAtUtc,
                c.UpdatedAtUtc,
                c.Version,
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw ClinicSettingsException.ClinicNotFound();

        var organizationName = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == clinic.OrganizationId)
            .Select(o => o.Name)
            .SingleOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        return new ClinicSettingsResponse
        {
            ClinicId = clinic.Id,
            OrganizationId = clinic.OrganizationId,
            OrganizationName = organizationName,
            Name = clinic.Name,
            Slug = clinic.Slug,
            Specialty = clinic.Specialty,
            ContactEmail = clinic.Email,
            ContactPhone = clinic.PhoneNumber,
            Address = clinic.AddressLine1 ?? clinic.Address,
            City = clinic.City,
            Country = clinic.Country,
            DefaultTimeZoneId = clinic.TimeZoneId,
            IsActive = clinic.IsActive,
            CreatedAtUtc = clinic.CreatedAtUtc,
            UpdatedAtUtc = clinic.UpdatedAtUtc,
            Version = clinic.Version,
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
            throw ClinicSettingsException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(permission);
    }

    private async Task<Guid> ResolveClinicIdAsync(
        ClinicSettingsQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.ClinicId is null || query.ClinicId == Guid.Empty)
            {
                throw ClinicSettingsException.ClinicScopeRequired();
            }

            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(c => c.Id == query.ClinicId.Value, cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "clinic_settings",
                    ClinicSettingsErrorCodes.ClinicNotFound,
                    null,
                    query.ClinicId);
                throw ClinicSettingsException.ClinicNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("clinic_settings", null, query.ClinicId);
            return query.ClinicId.Value;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (query.ClinicId is Guid requested
            && requested != Guid.Empty
            && requested != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied(
                "clinic_settings_clinic_override",
                ClinicSettingsErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                requested);
            throw ClinicSettingsException.InvalidScope();
        }

        if (_currentStaff.ClinicId == Guid.Empty)
        {
            throw ClinicSettingsException.ClinicNotFound();
        }

        return _currentStaff.ClinicId;
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

        throw ClinicSettingsException.InvalidTimezone();
    }
}
