using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Clinics;
using HealthCare.Application.Organizations;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Clinics;

/// <summary>
/// Organization-scoped clinic CRUD. Deactivation is soft: history is preserved; inactive clinics
/// block new membership activation, staff login for that clinic, and new appointment booking
/// (existing CurrentUserContext / AppointmentService rules). Staff can later be managed via Staff APIs.
/// Initial Clinic Admin is created in the same DB transaction (not via StaffManagementService.CreateAsync)
/// to avoid nested transactions while reusing Identity + StaffMember + role-assignment rules.
/// </summary>
public sealed class ClinicManagementService : IClinicManagementService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IRoleAssignmentAuthorizationService _roleAssignment;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly IOrganizationLimitService _limits;
    private readonly IClinicTimeZoneConverter _timeZones;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClinicManagementService> _logger;

    public ClinicManagementService(
        HealthCareDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IRoleAssignmentAuthorizationService roleAssignment,
        IAuthorizationAuditLogger audit,
        IOrganizationLimitService limits,
        IClinicTimeZoneConverter timeZones,
        TimeProvider timeProvider,
        ILogger<ClinicManagementService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _roleAssignment = roleAssignment;
        _audit = audit;
        _limits = limits;
        _timeZones = timeZones;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PagedResponse<OrganizationClinicListItemResponse>> SearchAsync(
        OrganizationClinicSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.Read);
        var organizationId = await ResolveOrganizationIdAsync(request.OrganizationId, bypass, cancellationToken);

        var query = _dbContext.Clinics.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || c.Slug.ToLower().Contains(term)
                || (c.Specialty != null && c.Specialty.ToLower().Contains(term))
                || (c.City != null && c.City.ToLower().Contains(term))
                || (c.Email != null && c.Email.ToLower().Contains(term))
                || (c.PhoneNumber != null && c.PhoneNumber.ToLower().Contains(term)));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(c => c.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var desc = request.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        query = request.SortBy.ToLowerInvariant() switch
        {
            "slug" => desc
                ? query.OrderByDescending(c => c.Slug).ThenBy(c => c.Id)
                : query.OrderBy(c => c.Slug).ThenBy(c => c.Id),
            "specialty" => desc
                ? query.OrderByDescending(c => c.Specialty).ThenBy(c => c.Id)
                : query.OrderBy(c => c.Specialty).ThenBy(c => c.Id),
            "city" => desc
                ? query.OrderByDescending(c => c.City).ThenBy(c => c.Id)
                : query.OrderBy(c => c.City).ThenBy(c => c.Id),
            "createdatutc" => desc
                ? query.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                : query.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id),
            "isactive" => desc
                ? query.OrderByDescending(c => c.IsActive).ThenBy(c => c.Name).ThenBy(c => c.Id)
                : query.OrderBy(c => c.IsActive).ThenBy(c => c.Name).ThenBy(c => c.Id),
            _ => desc
                ? query.OrderByDescending(c => c.Name).ThenBy(c => c.Id)
                : query.OrderBy(c => c.Name).ThenBy(c => c.Id),
        };

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? 20
            : Math.Min(request.PageSize, OrganizationClinicSearchRequestValidator.MaxPageSize);

        var clinics = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.OrganizationId,
                c.Name,
                c.Slug,
                c.Specialty,
                c.City,
                c.TimeZoneId,
                c.IsActive,
                c.Version,
            })
            .ToListAsync(cancellationToken);

        var clinicIds = clinics.Select(c => c.Id).ToList();
        var staffCounts = await LoadStaffCountsAsync(organizationId, clinicIds, cancellationToken);

        _logger.LogInformation(
            "Clinic management list accessed. ActorUserId={ActorUserId} OrganizationId={OrganizationId} ResultCount={ResultCount}",
            _currentUser.UserId,
            organizationId,
            clinics.Count);

        var items = clinics.Select(c =>
        {
            staffCounts.TryGetValue(c.Id, out var counts);
            return new OrganizationClinicListItemResponse
            {
                ClinicId = c.Id,
                OrganizationId = c.OrganizationId,
                Name = c.Name,
                Slug = c.Slug,
                Specialty = c.Specialty,
                City = c.City,
                TimeZoneId = c.TimeZoneId,
                IsActive = c.IsActive,
                Version = c.Version,
                ActiveStaffCount = counts.Staff,
                ActiveDoctorCount = counts.Doctors,
            };
        }).ToList();

        return PagedResponse<OrganizationClinicListItemResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<OrganizationClinicDetailResponse> GetByIdAsync(
        Guid clinicId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.Read);
        var clinic = await LoadScopedClinicAsync(clinicId, bypass, forUpdate: false, cancellationToken);
        var detail = await MapDetailAsync(clinic, cancellationToken);

        _logger.LogInformation(
            "Clinic management detail accessed. ActorUserId={ActorUserId} ClinicId={ClinicId}",
            _currentUser.UserId,
            clinic.Id);

        return detail;
    }

    public async Task<OrganizationClinicDetailResponse> CreateAsync(
        CreateOrganizationClinicRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.Create);
        var organizationId = await ResolveOrganizationIdAsync(request.OrganizationId, bypass, cancellationToken);
        await EnsureOrganizationActiveAsync(organizationId, cancellationToken);
        await _limits.EnsureClinicCapacityAsync(organizationId, cancellationToken);

        var slug = ClinicSlugRules.Normalize(request.Slug);
        if (!ClinicSlugRules.IsValid(slug))
        {
            throw ClinicManagementException.SlugInvalid();
        }

        EnsureValidTimezone(request.TimeZoneId);

        if (await _dbContext.Clinics.AnyAsync(c => c.Slug == slug, cancellationToken))
        {
            throw ClinicManagementException.SlugInUse();
        }

        var utcNow = _timeProvider.GetUtcNow();
        var clinic = new Domain.Clinics.Clinic
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            Slug = slug,
            Specialty = TrimOrNull(request.Specialty),
            PhoneNumber = TrimOrNull(request.PhoneNumber),
            Email = TrimOrNull(request.Email),
            AddressLine1 = TrimOrNull(request.AddressLine1),
            AddressLine2 = TrimOrNull(request.AddressLine2),
            City = TrimOrNull(request.City),
            Region = TrimOrNull(request.Region),
            PostalCode = TrimOrNull(request.PostalCode),
            Country = TrimOrNull(request.Country),
            Address = TrimOrNull(request.AddressLine1),
            TimeZoneId = request.TimeZoneId.Trim(),
            IsActive = true,
            Version = 0,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.Clinics.Add(clinic);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (request.InitialClinicAdmin is not null)
            {
                await CreateInitialClinicAdminAsync(clinic, request.InitialClinicAdmin, cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch (ClinicManagementException)
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            throw ClinicManagementException.SlugInUse();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            if (request.InitialClinicAdmin is not null)
            {
                _logger.LogWarning(ex, "Initial clinic admin failed during clinic create. ClinicId={ClinicId}", clinic.Id);
                throw ClinicManagementException.InitialAdminFailed("Initial Clinic Admin could not be created.");
            }

            throw;
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("clinic_create", organizationId, clinic.Id);
        }

        _audit.ClinicOperation("clinic_created", "succeeded", organizationId, clinic.Id);

        _logger.LogInformation(
            "Clinic created. ActorUserId={ActorUserId} OrganizationId={OrganizationId} ClinicId={ClinicId} Slug={Slug} HasInitialAdmin={HasInitialAdmin}",
            _currentUser.UserId,
            organizationId,
            clinic.Id,
            clinic.Slug,
            request.InitialClinicAdmin is not null);

        return await MapDetailAsync(clinic, cancellationToken);
    }

    public async Task<OrganizationClinicDetailResponse> UpdateAsync(
        Guid clinicId,
        UpdateOrganizationClinicRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.Update);
        var clinic = await LoadScopedClinicAsync(clinicId, bypass, forUpdate: true, cancellationToken);
        EnsureExpectedVersion(clinic, request.ExpectedVersion);

        var changed = new List<string>();
        if (request.Name is not null)
        {
            clinic.Name = request.Name.Trim();
            changed.Add(nameof(clinic.Name));
        }

        if (request.Slug is not null)
        {
            var slug = ClinicSlugRules.Normalize(request.Slug);
            if (!ClinicSlugRules.IsValid(slug))
            {
                throw ClinicManagementException.SlugInvalid();
            }

            if (!string.Equals(slug, clinic.Slug, StringComparison.Ordinal)
                && await _dbContext.Clinics.AnyAsync(c => c.Slug == slug && c.Id != clinic.Id, cancellationToken))
            {
                throw ClinicManagementException.SlugInUse();
            }

            clinic.Slug = slug;
            changed.Add(nameof(clinic.Slug));
        }

        if (request.Specialty is not null)
        {
            clinic.Specialty = TrimOrNull(request.Specialty);
            changed.Add(nameof(clinic.Specialty));
        }

        if (request.PhoneNumber is not null)
        {
            clinic.PhoneNumber = TrimOrNull(request.PhoneNumber);
            changed.Add(nameof(clinic.PhoneNumber));
        }

        if (request.Email is not null)
        {
            clinic.Email = TrimOrNull(request.Email);
            changed.Add(nameof(clinic.Email));
        }

        if (request.AddressLine1 is not null)
        {
            clinic.AddressLine1 = TrimOrNull(request.AddressLine1);
            clinic.Address = clinic.AddressLine1;
            changed.Add(nameof(clinic.AddressLine1));
        }

        if (request.AddressLine2 is not null)
        {
            clinic.AddressLine2 = TrimOrNull(request.AddressLine2);
            changed.Add(nameof(clinic.AddressLine2));
        }

        if (request.City is not null)
        {
            clinic.City = TrimOrNull(request.City);
            changed.Add(nameof(clinic.City));
        }

        if (request.Region is not null)
        {
            clinic.Region = TrimOrNull(request.Region);
            changed.Add(nameof(clinic.Region));
        }

        if (request.PostalCode is not null)
        {
            clinic.PostalCode = TrimOrNull(request.PostalCode);
            changed.Add(nameof(clinic.PostalCode));
        }

        if (request.Country is not null)
        {
            clinic.Country = TrimOrNull(request.Country);
            changed.Add(nameof(clinic.Country));
        }

        if (request.TimeZoneId is not null)
        {
            EnsureValidTimezone(request.TimeZoneId);
            clinic.TimeZoneId = request.TimeZoneId.Trim();
            changed.Add(nameof(clinic.TimeZoneId));
        }

        if (changed.Count == 0)
        {
            throw ClinicManagementException.EmptyUpdate();
        }

        clinic.Version++;
        clinic.UpdatedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ClinicManagementException.ConcurrencyConflict();
        }
        catch (DbUpdateException)
        {
            throw ClinicManagementException.SlugInUse();
        }

        _audit.ClinicOperation("clinic_updated", "succeeded", clinic.OrganizationId, clinic.Id);

        _logger.LogInformation(
            "Clinic updated. ActorUserId={ActorUserId} ClinicId={ClinicId} ChangedFields={ChangedFields}",
            _currentUser.UserId,
            clinic.Id,
            string.Join(',', changed));

        return await MapDetailAsync(clinic, cancellationToken);
    }

    public async Task<OrganizationClinicDetailResponse> ActivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.Activate);
        var clinic = await LoadScopedClinicAsync(clinicId, bypass, forUpdate: true, cancellationToken);
        EnsureExpectedVersion(clinic, request.ExpectedVersion);
        await EnsureOrganizationActiveAsync(clinic.OrganizationId, cancellationToken);

        if (clinic.IsActive)
        {
            // Idempotent: already active.
            return await MapDetailAsync(clinic, cancellationToken);
        }

        clinic.IsActive = true;
        clinic.Version++;
        clinic.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken);

        _audit.ClinicOperation("clinic_activated", "succeeded", clinic.OrganizationId, clinic.Id);

        _logger.LogInformation(
            "Clinic activated. ActorUserId={ActorUserId} ClinicId={ClinicId} ReasonPresent={ReasonPresent}",
            _currentUser.UserId,
            clinic.Id,
            !string.IsNullOrWhiteSpace(request.Reason));

        return await MapDetailAsync(clinic, cancellationToken);
    }

    public async Task<OrganizationClinicDetailResponse> DeactivateAsync(
        Guid clinicId,
        ClinicActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Clinics.Deactivate);
        var clinic = await LoadScopedClinicAsync(clinicId, bypass, forUpdate: true, cancellationToken);
        EnsureExpectedVersion(clinic, request.ExpectedVersion);

        if (!clinic.IsActive)
        {
            // Idempotent: already inactive.
            return await MapDetailAsync(clinic, cancellationToken);
        }

        var otherActive = await _dbContext.Clinics.CountAsync(
            c => c.OrganizationId == clinic.OrganizationId
                 && c.IsActive
                 && c.Id != clinic.Id,
            cancellationToken);
        if (otherActive == 0)
        {
            throw ClinicManagementException.DeactivationNotAllowed(
                "Cannot deactivate the last active clinic in the organization.");
        }

        clinic.IsActive = false;
        clinic.Version++;
        clinic.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken);

        _audit.ClinicOperation("clinic_deactivated", "succeeded", clinic.OrganizationId, clinic.Id);

        _logger.LogInformation(
            "Clinic deactivated. ActorUserId={ActorUserId} ClinicId={ClinicId} ReasonPresent={ReasonPresent}",
            _currentUser.UserId,
            clinic.Id,
            !string.IsNullOrWhiteSpace(request.Reason));

        return await MapDetailAsync(clinic, cancellationToken);
    }

    private async Task CreateInitialClinicAdminAsync(
        Domain.Clinics.Clinic clinic,
        CreateClinicInitialAdminRequest admin,
        CancellationToken cancellationToken)
    {
        _permissions.RequirePermission(Permissions.Staff.Manage);
        _permissions.RequirePermission(Permissions.Roles.Assign);
        await _limits.EnsureStaffCapacityAsync(clinic.OrganizationId, cancellationToken);

        var email = admin.Email.Trim();
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            throw ClinicManagementException.InitialAdminFailed("Initial Clinic Admin email is already in use.");
        }

        var actorRole = _currentStaff.HasActiveMembership
            ? _currentStaff.Role
            : AppRoles.PlatformAdmin;

        try
        {
            _roleAssignment.EnsureCanAssignRole(new RoleAssignmentRequest(
                ActorUserId: _currentUser.UserId ?? Guid.Empty,
                ActorRole: actorRole,
                ActorOrganizationId: _currentStaff.HasActiveMembership ? _currentStaff.OrganizationId : clinic.OrganizationId,
                ActorClinicId: _currentStaff.HasActiveMembership ? _currentStaff.ClinicId : null,
                TargetUserId: Guid.NewGuid(),
                TargetRole: AppRoles.ClinicAdmin,
                TargetOrganizationId: clinic.OrganizationId,
                TargetClinicId: clinic.Id,
                TargetHasPatientRole: false,
                TargetHasStaffMembership: false));
        }
        catch (AuthorizationException)
        {
            throw ClinicManagementException.InitialAdminFailed("Initial Clinic Admin role assignment is not allowed.");
        }

        var utcNow = _timeProvider.GetUtcNow();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        };

        var createResult = await _userManager.CreateAsync(user, admin.TemporaryPassword);
        if (!createResult.Succeeded)
        {
            throw ClinicManagementException.InitialAdminFailed(
                string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        var roleResult = await _userManager.AddToRoleAsync(user, AppRoles.ClinicAdmin);
        if (!roleResult.Succeeded)
        {
            throw ClinicManagementException.InitialAdminFailed(
                string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        _dbContext.StaffMembers.Add(new StaffMember
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrganizationId = clinic.OrganizationId,
            ClinicId = clinic.Id,
            Role = AppRoles.ClinicAdmin,
            FirstName = admin.FirstName.Trim(),
            LastName = admin.LastName.Trim(),
            JobTitle = TrimOrNull(admin.JobTitle),
            IsActive = true,
            Version = 0,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Initial Clinic Admin created. ActorUserId={ActorUserId} ClinicId={ClinicId} TargetUserId={TargetUserId}",
            _currentUser.UserId,
            clinic.Id,
            user.Id);
    }

    private void EnsureAuthorized(string permission)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw ClinicManagementException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(permission);

        // Organization clinic management is org-admin (or platform bypass) only.
        if (_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return;
        }

        if (!_currentStaff.HasActiveMembership || _currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw ClinicManagementException.AccessDenied();
        }
    }

    private async Task<Guid> ResolveOrganizationIdAsync(
        Guid? requestedOrganizationId,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (requestedOrganizationId is null || requestedOrganizationId == Guid.Empty)
            {
                throw ClinicManagementException.OrganizationScopeRequired();
            }

            var exists = await _dbContext.Organizations.AsNoTracking()
                .AnyAsync(o => o.Id == requestedOrganizationId.Value, cancellationToken);
            if (!exists)
            {
                throw ClinicManagementException.NotFound();
            }

            _audit.ExplicitPlatformBypassUsed("clinic_management", requestedOrganizationId, null);
            return requestedOrganizationId.Value;
        }

        if (!_currentStaff.HasActiveMembership || _currentStaff.Role != AppRoles.OrganizationAdmin)
        {
            throw ClinicManagementException.AccessDenied();
        }

        if (requestedOrganizationId is Guid clientOrg
            && clientOrg != Guid.Empty
            && clientOrg != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "clinic_management_org_override",
                ClinicManagementErrorCodes.InvalidScope,
                clientOrg,
                null);
            throw ClinicManagementException.InvalidScope();
        }

        return _currentStaff.OrganizationId;
    }

    private async Task<Domain.Clinics.Clinic> LoadScopedClinicAsync(
        Guid clinicId,
        PlatformAdminBypass bypass,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var query = forUpdate
            ? _dbContext.Clinics.AsQueryable()
            : _dbContext.Clinics.AsNoTracking();

        var clinic = await query.SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken);
        if (clinic is null)
        {
            throw ClinicManagementException.NotFound();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("clinic_management_detail", clinic.OrganizationId, clinic.Id);
            return clinic;
        }

        if (!_currentStaff.HasActiveMembership
            || _currentStaff.Role != AppRoles.OrganizationAdmin
            || clinic.OrganizationId != _currentStaff.OrganizationId)
        {
            _audit.CrossTenantDenied(
                "clinic_management_detail",
                ClinicManagementErrorCodes.NotFound,
                clinic.OrganizationId,
                clinic.Id);
            throw ClinicManagementException.NotFound();
        }

        return clinic;
    }

    private async Task EnsureOrganizationActiveAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var status = await _dbContext.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => (OrganizationStatus?)o.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status is null)
        {
            throw ClinicManagementException.NotFound();
        }

        if (status != OrganizationStatus.Active)
        {
            throw ClinicManagementException.InactiveOrganization();
        }
    }

    private void EnsureValidTimezone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw ClinicManagementException.InvalidTimezone();
        }

        var id = timeZoneId.Trim();
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return;
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows hosts may only know Arab Standard Time for Riyadh.
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

        throw ClinicManagementException.InvalidTimezone();
    }

    private async Task<OrganizationClinicDetailResponse> MapDetailAsync(
        Domain.Clinics.Clinic clinic,
        CancellationToken cancellationToken)
    {
        var counts = await LoadStaffCountsAsync(clinic.OrganizationId, [clinic.Id], cancellationToken);
        counts.TryGetValue(clinic.Id, out var staff);

        var today = _timeZones.GetClinicDate(_timeProvider.GetUtcNow(), clinic.TimeZoneId);
        var dayStart = _timeZones.ToUtc(today, TimeOnly.MinValue, clinic.TimeZoneId);
        var dayEnd = _timeZones.ToUtc(today.AddDays(1), TimeOnly.MinValue, clinic.TimeZoneId);
        var appointmentCountToday = await _dbContext.Appointments.AsNoTracking()
            .CountAsync(
                a => a.ClinicId == clinic.Id
                     && a.AppointmentDateUtc >= dayStart
                     && a.AppointmentDateUtc < dayEnd,
                cancellationToken);

        return new OrganizationClinicDetailResponse
        {
            ClinicId = clinic.Id,
            OrganizationId = clinic.OrganizationId,
            Name = clinic.Name,
            Slug = clinic.Slug,
            Specialty = clinic.Specialty,
            PhoneNumber = clinic.PhoneNumber,
            Email = clinic.Email,
            AddressLine1 = clinic.AddressLine1 ?? clinic.Address,
            AddressLine2 = clinic.AddressLine2,
            City = clinic.City,
            Region = clinic.Region,
            PostalCode = clinic.PostalCode,
            Country = clinic.Country,
            TimeZoneId = clinic.TimeZoneId,
            IsActive = clinic.IsActive,
            CreatedAtUtc = clinic.CreatedAtUtc,
            UpdatedAtUtc = clinic.UpdatedAtUtc,
            Version = clinic.Version,
            ActiveStaffCount = staff.Staff,
            ActiveDoctorCount = staff.Doctors,
            AppointmentCountToday = appointmentCountToday,
        };
    }

    private async Task<Dictionary<Guid, (int Staff, int Doctors)>> LoadStaffCountsAsync(
        Guid organizationId,
        IReadOnlyList<Guid> clinicIds,
        CancellationToken cancellationToken)
    {
        if (clinicIds.Count == 0)
        {
            return new Dictionary<Guid, (int, int)>();
        }

        var rows = await _dbContext.StaffMembers.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                && clinicIds.Contains(s.ClinicId)
                && s.IsActive)
            .GroupBy(s => s.ClinicId)
            .Select(g => new
            {
                ClinicId = g.Key,
                Staff = g.Count(),
                Doctors = g.Count(s => s.Role == AppRoles.Doctor),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.ClinicId, x => (x.Staff, x.Doctors));
    }

    private static void EnsureExpectedVersion(Domain.Clinics.Clinic clinic, int expectedVersion)
    {
        if (clinic.Version != expectedVersion)
        {
            throw ClinicManagementException.ConcurrencyConflict();
        }
    }

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
