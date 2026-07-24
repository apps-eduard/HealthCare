using HealthCare.Application.Authorization;
using HealthCare.Application.Identity;
using HealthCare.Application.Staff;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Staff;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Staff;

public sealed class StaffManagementService : IStaffManagementService
{
    private static readonly HashSet<string> AdminRoles = new(StringComparer.Ordinal)
    {
        AppRoles.ClinicAdmin,
        AppRoles.OrganizationAdmin,
        AppRoles.PlatformAdmin,
    };

    private readonly HealthCareDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IRoleAssignmentAuthorizationService _roleAssignment;
    private readonly ISecuritySessionInvalidationService _sessions;
    private readonly IAccountEmailSender _emailSender;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StaffManagementService> _logger;

    private const string PasswordResetGenericMessage =
        "If the account is eligible, a password reset message has been sent.";

    public StaffManagementService(
        HealthCareDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IRoleAssignmentAuthorizationService roleAssignment,
        ISecuritySessionInvalidationService sessions,
        IAccountEmailSender emailSender,
        IAuthorizationAuditLogger audit,
        TimeProvider timeProvider,
        ILogger<StaffManagementService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _roleAssignment = roleAssignment;
        _sessions = sessions;
        _emailSender = emailSender;
        _audit = audit;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PagedResponse<StaffSummaryResponse>> SearchAsync(
        StaffSearchRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Staff.Read);
        var scope = await ResolveScopeAsync(request.ClinicId, bypass, cancellationToken);

        var query =
            from s in ApplyStaffScope(_dbContext.StaffMembers.AsNoTracking(), scope)
            join u in _dbContext.Users.AsNoTracking() on s.UserId equals u.Id
            join c in _dbContext.Clinics.AsNoTracking() on s.ClinicId equals c.Id
            select new { Staff = s, User = u, Clinic = c };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Staff.FirstName.ToLower().Contains(term)
                || x.Staff.LastName.ToLower().Contains(term)
                || (x.Staff.DisplayName != null && x.Staff.DisplayName.ToLower().Contains(term))
                || (x.User.Email != null && x.User.Email.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            query = query.Where(x => x.Staff.Role == request.Role);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(x => x.Staff.IsActive == request.IsActive.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var desc = request.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = request.SortBy.ToLowerInvariant();

        query = sortBy switch
        {
            "firstname" => desc
                ? query.OrderByDescending(x => x.Staff.FirstName).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.Staff.FirstName).ThenBy(x => x.Staff.Id),
            "email" => desc
                ? query.OrderByDescending(x => x.User.Email).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.User.Email).ThenBy(x => x.Staff.Id),
            "role" => desc
                ? query.OrderByDescending(x => x.Staff.Role).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.Staff.Role).ThenBy(x => x.Staff.Id),
            "createdatutc" => desc
                ? query.OrderByDescending(x => x.Staff.CreatedAtUtc).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.Staff.CreatedAtUtc).ThenBy(x => x.Staff.Id),
            "updatedatutc" => desc
                ? query.OrderByDescending(x => x.Staff.UpdatedAtUtc).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.Staff.UpdatedAtUtc).ThenBy(x => x.Staff.Id),
            "jobtitle" => desc
                ? query.OrderByDescending(x => x.Staff.JobTitle).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.Staff.JobTitle).ThenBy(x => x.Staff.Id),
            "displayname" => desc
                ? query.OrderByDescending(x => x.Staff.DisplayName).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.Staff.DisplayName).ThenBy(x => x.Staff.Id),
            _ => desc
                ? query.OrderByDescending(x => x.Staff.LastName).ThenBy(x => x.Staff.Id)
                : query.OrderBy(x => x.Staff.LastName).ThenBy(x => x.Staff.Id),
        };

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1
            ? StaffSearchRequestValidator.DefaultPageSize
            : Math.Min(request.PageSize, StaffSearchRequestValidator.MaxPageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new StaffSummaryResponse
            {
                StaffMemberId = x.Staff.Id,
                UserId = x.Staff.UserId,
                Email = x.User.Email ?? string.Empty,
                FirstName = x.Staff.FirstName,
                LastName = x.Staff.LastName,
                DisplayName = x.Staff.DisplayName,
                JobTitle = x.Staff.JobTitle,
                OrganizationId = x.Staff.OrganizationId,
                ClinicId = x.Staff.ClinicId,
                ClinicName = x.Clinic.Name,
                Role = x.Staff.Role,
                MembershipIsActive = x.Staff.IsActive,
                AccountIsActive = x.User.IsActive,
                UpdatedAtUtc = x.Staff.UpdatedAtUtc,
                Version = x.Staff.Version,
            })
            .ToListAsync(cancellationToken);

        return PagedResponse<StaffSummaryResponse>.Create(items, page, pageSize, totalCount);
    }

    public async Task<StaffDetailResponse> GetByIdAsync(
        Guid staffMemberId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Staff.Read);
        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: false, cancellationToken);
        var user = await _dbContext.Users.AsNoTracking().SingleAsync(u => u.Id == staff.UserId, cancellationToken);
        var clinicName = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == staff.ClinicId)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancellationToken);
        return MapDetail(staff, user, clinicName);
    }

    public async Task<CreateStaffResponse> CreateAsync(
        CreateStaffRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Staff.Manage);
        _permissions.RequirePermission(Permissions.Roles.Assign);

        var clinic = await ResolveTargetClinicForCreateAsync(request.ClinicId, bypass, cancellationToken);
        if (clinic.Organization is null || clinic.Organization.Status != OrganizationStatus.Active)
        {
            throw StaffManagementException.InactiveOrganization();
        }

        if (!clinic.IsActive)
        {
            throw StaffManagementException.InactiveClinic();
        }

        var role = request.Role.Trim();
        if (!IsAssignableStaffRole(role))
        {
            throw StaffManagementException.InvalidRole();
        }

        var futureUserId = Guid.NewGuid();
        EnsureRoleAssignmentAllowed(
            futureUserId,
            role,
            clinic.OrganizationId,
            clinic.Id,
            targetHasPatientRole: false,
            targetHasStaffMembership: false);

        var email = request.Email.Trim();
        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            throw StaffManagementException.EmailInUse();
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        ApplicationUser? createdUser = null;
        try
        {
            var utcNow = _timeProvider.GetUtcNow();
            createdUser = new ApplicationUser
            {
                Id = futureUserId,
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
                IsActive = true,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow,
            };

            var createResult = await _userManager.CreateAsync(createdUser, request.TemporaryPassword);
            if (!createResult.Succeeded)
            {
                throw StaffManagementException.CreationFailed(
                    string.Join("; ", createResult.Errors.Select(e => e.Description)));
            }

            var roleResult = await _userManager.AddToRoleAsync(createdUser, role);
            if (!roleResult.Succeeded)
            {
                throw StaffManagementException.CreationFailed(
                    string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }

            var staff = new StaffMember
            {
                Id = Guid.NewGuid(),
                UserId = createdUser.Id,
                OrganizationId = clinic.OrganizationId,
                ClinicId = clinic.Id,
                Role = role,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                    ? null
                    : request.DisplayName.Trim(),
                JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim(),
                IsActive = true,
                Version = 0,
                CreatedAtUtc = utcNow,
                UpdatedAtUtc = utcNow,
            };

            _dbContext.StaffMembers.Add(staff);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Staff created. ActorUserId={ActorUserId} TargetStaffMemberId={StaffMemberId} TargetUserId={TargetUserId} OrganizationId={OrganizationId} ClinicId={ClinicId} Role={Role}",
                _currentUser.UserId,
                staff.Id,
                staff.UserId,
                staff.OrganizationId,
                staff.ClinicId,
                staff.Role);

            return new CreateStaffResponse { Staff = MapDetail(staff, createdUser, clinic.Name) };
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            if (createdUser is not null)
            {
                await _userManager.DeleteAsync(createdUser);
            }

            throw;
        }
    }

    public async Task<StaffDetailResponse> UpdateAsync(
        Guid staffMemberId,
        UpdateStaffRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Staff.Manage);
        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: true, cancellationToken);
        EnsureExpectedVersion(staff, request.ExpectedVersion);
        EnsureCanMutateTarget(staff);

        if (request.FirstName is not null)
        {
            staff.FirstName = request.FirstName.Trim();
        }

        if (request.LastName is not null)
        {
            staff.LastName = request.LastName.Trim();
        }

        if (request.DisplayName is not null)
        {
            staff.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        }

        if (request.JobTitle is not null)
        {
            staff.JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim();
        }

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw StaffManagementException.NotFound();

        if (request.PhoneNumber is not null)
        {
            user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            user.UpdatedAtUtc = _timeProvider.GetUtcNow();
        }

        staff.Version++;
        staff.UpdatedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw StaffManagementException.ConcurrencyConflict();
        }

        _logger.LogInformation(
            "Staff profile updated. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId}",
            _currentUser.UserId,
            staff.Id);

        return await MapDetailAsync(staff, user, cancellationToken);
    }

    public async Task<StaffDetailResponse> ActivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Staff.Manage);
        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: true, cancellationToken);
        EnsureExpectedVersion(staff, request.ExpectedVersion);
        EnsureCanMutateTarget(staff);

        if (staff.IsActive)
        {
            throw StaffManagementException.AlreadyActive();
        }

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw StaffManagementException.NotFound();

        staff.IsActive = true;
        staff.Version++;
        staff.UpdatedAtUtc = _timeProvider.GetUtcNow();
        user.IsActive = true;
        user.UpdatedAtUtc = staff.UpdatedAtUtc;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw StaffManagementException.ConcurrencyConflict();
        }

        await _sessions.InvalidateUserSessionsAsync(staff.UserId, "StaffActivated", cancellationToken);

        _logger.LogInformation(
            "Staff activated. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} Reason={Reason}",
            _currentUser.UserId,
            staff.Id,
            request.Reason);

        return await MapDetailAsync(staff, user, cancellationToken);
    }

    public async Task<StaffDetailResponse> DeactivateAsync(
        Guid staffMemberId,
        StaffActivationRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Staff.Manage);
        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: true, cancellationToken);
        EnsureExpectedVersion(staff, request.ExpectedVersion);
        EnsureCanMutateTarget(staff);

        if (!staff.IsActive)
        {
            throw StaffManagementException.AlreadyInactive();
        }

        if (_currentUser.UserId == staff.UserId)
        {
            throw StaffManagementException.SelfDeactivationDenied();
        }

        await EnsureNotLastAdminAsync(staff, cancellationToken);

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw StaffManagementException.NotFound();

        staff.IsActive = false;
        staff.Version++;
        staff.UpdatedAtUtc = _timeProvider.GetUtcNow();
        user.IsActive = false;
        user.UpdatedAtUtc = staff.UpdatedAtUtc;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw StaffManagementException.ConcurrencyConflict();
        }

        await _sessions.InvalidateUserSessionsAsync(staff.UserId, "StaffDeactivated", cancellationToken);

        _logger.LogInformation(
            "Staff deactivated. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} Reason={Reason}",
            _currentUser.UserId,
            staff.Id,
            request.Reason);

        return await MapDetailAsync(staff, user, cancellationToken);
    }

    public Task<IReadOnlyList<StaffRoleInfoResponse>> ListAssignableRolesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Roles.Read);

        Guid? orgId = _currentStaff.HasActiveMembership ? _currentStaff.OrganizationId : null;
        Guid? clinicId = _currentStaff.HasActiveMembership ? _currentStaff.ClinicId : null;

        var roles = AppRoles.All
            .Where(r => r != AppRoles.Patient)
            .Select(role =>
            {
                var assignable = false;
                try
                {
                    EnsureRoleAssignmentAllowed(
                        Guid.NewGuid(),
                        role,
                        orgId ?? Guid.Empty,
                        clinicId ?? Guid.Empty,
                        targetHasPatientRole: false,
                        targetHasStaffMembership: false);
                    assignable = true;
                }
                catch (AuthorizationException)
                {
                    assignable = false;
                }
                catch (StaffManagementException)
                {
                    assignable = false;
                }

                return new StaffRoleInfoResponse
                {
                    Name = role,
                    DisplayLabel = role.Replace('_', ' '),
                    AssignableByCurrentUser = assignable,
                };
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<StaffRoleInfoResponse>>(roles);
    }

    public async Task AssignRoleAsync(
        Guid staffMemberId,
        string roleName,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Roles.Assign);
        var role = roleName.Trim();
        if (!IsAssignableStaffRole(role))
        {
            throw StaffManagementException.InvalidRole();
        }

        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: true, cancellationToken);
        EnsureCanMutateTarget(staff);

        if (_currentUser.UserId == staff.UserId)
        {
            throw StaffManagementException.SelfElevationDenied();
        }

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw StaffManagementException.NotFound();

        var hasPatient = await _dbContext.Patients.AsNoTracking()
            .AnyAsync(p => p.UserId == staff.UserId && p.IsActive, cancellationToken);

        EnsureRoleAssignmentAllowed(
            staff.UserId,
            role,
            staff.OrganizationId,
            staff.ClinicId,
            targetHasPatientRole: hasPatient,
            targetHasStaffMembership: true);

        if (string.Equals(staff.Role, role, StringComparison.Ordinal))
        {
            return;
        }

        if (AdminRoles.Contains(staff.Role) && !AdminRoles.Contains(role))
        {
            await EnsureNotLastAdminAsync(staff, cancellationToken);
        }

        var identityRoles = await _userManager.GetRolesAsync(user);
        foreach (var existing in identityRoles.Where(r => r != AppRoles.Patient))
        {
            await _userManager.RemoveFromRoleAsync(user, existing);
        }

        var add = await _userManager.AddToRoleAsync(user, role);
        if (!add.Succeeded)
        {
            throw StaffManagementException.RoleAssignmentDenied();
        }

        staff.Role = role;
        staff.Version++;
        staff.UpdatedAtUtc = _timeProvider.GetUtcNow();
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _sessions.InvalidateUserSessionsAsync(staff.UserId, "StaffRoleAssigned", cancellationToken);

        _logger.LogInformation(
            "Staff role assigned. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} Role={Role}",
            _currentUser.UserId,
            staff.Id,
            role);
    }

    public async Task RemoveRoleAsync(
        Guid staffMemberId,
        string roleName,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Roles.Assign);
        var role = roleName.Trim();
        if (!IsAssignableStaffRole(role))
        {
            throw StaffManagementException.InvalidRole();
        }

        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: true, cancellationToken);
        EnsureCanMutateTarget(staff);

        if (_currentUser.UserId == staff.UserId)
        {
            throw StaffManagementException.SelfElevationDenied();
        }

        if (!string.Equals(staff.Role, role, StringComparison.Ordinal))
        {
            return;
        }

        EnsureRoleAssignmentAllowed(
            staff.UserId,
            role,
            staff.OrganizationId,
            staff.ClinicId,
            targetHasPatientRole: false,
            targetHasStaffMembership: true);

        await EnsureNotLastAdminAsync(staff, cancellationToken);

        // MVP: membership requires a staff role — removing the sole membership role is not supported.
        // Callers should reassign to another permitted role instead of leaving staff without a role.
        throw StaffManagementException.RoleAssignmentDenied();
    }

    public async Task<StaffDetailResponse> ChangeClinicAsync(
        Guid staffMemberId,
        ChangeStaffClinicRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.Staff.Manage);
        EnsureCanChangeClinicActor();

        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: true, cancellationToken);
        EnsureExpectedVersion(staff, request.ExpectedVersion);
        EnsureCanMutateTarget(staff);

        if (staff.Role is AppRoles.PlatformAdmin or AppRoles.OrganizationAdmin)
        {
            throw StaffManagementException.ClinicChangeNotAllowed(
                "Organization and platform administrators cannot be reassigned via clinic change.");
        }

        if (staff.ClinicId == request.NewClinicId)
        {
            var currentUser = await _userManager.FindByIdAsync(staff.UserId.ToString())
                ?? throw StaffManagementException.NotFound();
            return await MapDetailAsync(staff, currentUser, cancellationToken);
        }

        var targetClinic = await ResolveClinicInOrganizationAsync(
            request.NewClinicId,
            staff.OrganizationId,
            bypass,
            cancellationToken);

        if (!targetClinic.IsActive)
        {
            throw StaffManagementException.InactiveClinic();
        }

        if (targetClinic.Organization is null || targetClinic.Organization.Status != OrganizationStatus.Active)
        {
            throw StaffManagementException.InactiveOrganization();
        }

        if (staff.Role == AppRoles.ClinicAdmin)
        {
            await EnsureNotLastAdminAsync(staff, cancellationToken);
        }

        var previousClinicId = staff.ClinicId;
        staff.ClinicId = targetClinic.Id;
        staff.Version++;
        staff.UpdatedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw StaffManagementException.ConcurrencyConflict();
        }

        await _sessions.InvalidateUserSessionsAsync(staff.UserId, "StaffClinicChanged", cancellationToken);

        _logger.LogInformation(
            "Staff clinic changed. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} FromClinicId={FromClinicId} ToClinicId={ToClinicId} Reason={Reason}",
            _currentUser.UserId,
            staff.Id,
            previousClinicId,
            staff.ClinicId,
            request.AdministrativeReason);

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw StaffManagementException.NotFound();
        return MapDetail(staff, user, targetClinic.Name);
    }

    public async Task<StaffPasswordResetResponse> RequestPasswordResetAsync(
        Guid staffMemberId,
        StaffPasswordResetRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsurePasswordResetPermission();
        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: false, cancellationToken);
        EnsureCanMutateTarget(staff);

        if (staff.Role == AppRoles.PlatformAdmin
            && !(_currentUser.IsInRole(AppRoles.PlatformAdmin) && bypass == PlatformAdminBypass.Explicit))
        {
            throw StaffManagementException.PasswordResetNotAllowed();
        }

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw StaffManagementException.NotFound();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        await _emailSender.SendPasswordResetAsync(user.Email ?? string.Empty, token, cancellationToken);
        await _sessions.InvalidateUserSessionsAsync(staff.UserId, "StaffPasswordResetRequested", cancellationToken);

        _logger.LogInformation(
            "Staff password reset initiated. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} Reason={Reason}",
            _currentUser.UserId,
            staff.Id,
            request.Reason);

        return new StaffPasswordResetResponse { Message = PasswordResetGenericMessage };
    }

    public async Task<RevokeStaffSessionsResponse> RevokeSessionsAsync(
        Guid staffMemberId,
        RevokeStaffSessionsRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedStaffManager(Permissions.SecuritySessions.Revoke);
        var staff = await LoadScopedStaffAsync(staffMemberId, bypass, track: false, cancellationToken);
        EnsureCanMutateTarget(staff);

        if (staff.Role == AppRoles.PlatformAdmin
            && !(_currentUser.IsInRole(AppRoles.PlatformAdmin) && bypass == PlatformAdminBypass.Explicit))
        {
            throw StaffManagementException.DeactivationNotAllowed();
        }

        await _sessions.InvalidateUserSessionsAsync(staff.UserId, "StaffSessionsRevoked", cancellationToken);

        _logger.LogInformation(
            "Staff sessions revoked. ActorUserId={ActorUserId} StaffMemberId={StaffMemberId} Reason={Reason}",
            _currentUser.UserId,
            staff.Id,
            request.Reason);

        return new RevokeStaffSessionsResponse
        {
            Message = "Active sessions were revoked.",
        };
    }

    private void EnsureAuthenticatedStaffManager(string permission)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.Forbidden();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(permission);
    }

    private async Task<StaffScope> ResolveScopeAsync(
        Guid? requestedClinicId,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("staff_management", null, requestedClinicId);
            if (requestedClinicId is null || requestedClinicId == Guid.Empty)
            {
                // Same pattern as staff-patient search: clinic scope required for platform bypass.
                throw StaffManagementException.CrossTenantDenied();
            }

            var clinic = await _dbContext.Clinics.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == requestedClinicId.Value, cancellationToken)
                ?? throw StaffManagementException.NotFound();

            return new StaffScope(clinic.OrganizationId, clinic.Id, OrgWide: false);
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            if (requestedClinicId is Guid clinicId && clinicId != Guid.Empty)
            {
                var clinic = await _dbContext.Clinics.AsNoTracking()
                    .SingleOrDefaultAsync(
                        c => c.Id == clinicId && c.OrganizationId == _currentStaff.OrganizationId,
                        cancellationToken)
                    ?? throw StaffManagementException.NotFound();

                return new StaffScope(_currentStaff.OrganizationId, clinic.Id, OrgWide: false);
            }

            return new StaffScope(_currentStaff.OrganizationId, null, OrgWide: true);
        }

        // Clinic-scoped admins and other staff with staff.read: trusted clinic only.
        return new StaffScope(_currentStaff.OrganizationId, _currentStaff.ClinicId, OrgWide: false);
    }

    private static IQueryable<StaffMember> ApplyStaffScope(IQueryable<StaffMember> query, StaffScope scope)
    {
        query = query.Where(s => s.OrganizationId == scope.OrganizationId);
        if (!scope.OrgWide && scope.ClinicId is Guid clinicId)
        {
            query = query.Where(s => s.ClinicId == clinicId);
        }

        return query;
    }

    private async Task<StaffMember> LoadScopedStaffAsync(
        Guid staffMemberId,
        PlatformAdminBypass bypass,
        bool track,
        CancellationToken cancellationToken)
    {
        var query = track
            ? _dbContext.StaffMembers.AsQueryable()
            : _dbContext.StaffMembers.AsNoTracking();

        var staff = await query.SingleOrDefaultAsync(s => s.Id == staffMemberId, cancellationToken);
        if (staff is null)
        {
            throw StaffManagementException.NotFound();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _audit.ExplicitPlatformBypassUsed("staff_management_detail", staff.OrganizationId, staff.ClinicId);
            return staff;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            if (staff.OrganizationId != _currentStaff.OrganizationId)
            {
                _audit.CrossTenantDenied("staff_detail", StaffErrorCodes.CrossTenantDenied, staff.OrganizationId, staff.ClinicId);
                throw StaffManagementException.NotFound();
            }

            return staff;
        }

        if (staff.ClinicId != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied("staff_detail", StaffErrorCodes.CrossTenantDenied, staff.OrganizationId, staff.ClinicId);
            throw StaffManagementException.NotFound();
        }

        return staff;
    }

    private async Task<Domain.Clinics.Clinic> ResolveTargetClinicForCreateAsync(
        Guid? requestedClinicId,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (requestedClinicId is null || requestedClinicId == Guid.Empty)
            {
                throw StaffManagementException.NotFound();
            }

            _audit.ExplicitPlatformBypassUsed("staff_create", null, requestedClinicId);
            return await _dbContext.Clinics
                .Include(c => c.Organization)
                .SingleOrDefaultAsync(c => c.Id == requestedClinicId.Value, cancellationToken)
                ?? throw StaffManagementException.NotFound();
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            if (requestedClinicId is null || requestedClinicId == Guid.Empty)
            {
                throw StaffManagementException.NotFound();
            }

            return await _dbContext.Clinics
                .Include(c => c.Organization)
                .SingleOrDefaultAsync(
                    c => c.Id == requestedClinicId.Value && c.OrganizationId == _currentStaff.OrganizationId,
                    cancellationToken)
                ?? throw StaffManagementException.NotFound();
        }

        // Clinic admin (and other managers with staff.manage): own clinic only.
        return await _dbContext.Clinics
            .Include(c => c.Organization)
            .SingleAsync(c => c.Id == _currentStaff.ClinicId, cancellationToken);
    }

    private void EnsureRoleAssignmentAllowed(
        Guid targetUserId,
        string targetRole,
        Guid targetOrganizationId,
        Guid targetClinicId,
        bool targetHasPatientRole,
        bool targetHasStaffMembership)
    {
        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        var actorRole = _currentStaff.HasActiveMembership
            ? _currentStaff.Role
            : AppRoles.PlatformAdmin;

        try
        {
            _roleAssignment.EnsureCanAssignRole(new RoleAssignmentRequest(
                ActorUserId: _currentUser.UserId ?? Guid.Empty,
                ActorRole: actorRole,
                ActorOrganizationId: _currentStaff.HasActiveMembership ? _currentStaff.OrganizationId : null,
                ActorClinicId: _currentStaff.HasActiveMembership ? _currentStaff.ClinicId : null,
                TargetUserId: targetUserId,
                TargetRole: targetRole,
                TargetOrganizationId: targetOrganizationId,
                TargetClinicId: targetClinicId,
                TargetHasPatientRole: targetHasPatientRole,
                TargetHasStaffMembership: targetHasStaffMembership));
        }
        catch (AuthorizationException ex) when (ex.ErrorCode.Contains("role_assignment", StringComparison.Ordinal)
                                                 || ex.ErrorCode.Contains("permission", StringComparison.Ordinal))
        {
            throw StaffManagementException.RoleAssignmentDenied();
        }
    }

    private void EnsureCanMutateTarget(StaffMember target)
    {
        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        var actorRole = _currentStaff.HasActiveMembership
            ? _currentStaff.Role
            : AppRoles.PlatformAdmin;

        if (actorRole == AppRoles.ClinicAdmin
            && target.Role is AppRoles.OrganizationAdmin or AppRoles.PlatformAdmin)
        {
            throw StaffManagementException.DeactivationNotAllowed();
        }

        if (actorRole == AppRoles.OrganizationAdmin && target.Role == AppRoles.PlatformAdmin)
        {
            throw StaffManagementException.DeactivationNotAllowed();
        }
    }

    private void EnsurePasswordResetPermission()
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (!_permissions.HasPermission(Permissions.Staff.PasswordReset)
            && !_permissions.HasPermission(Permissions.Staff.Manage))
        {
            _permissions.RequirePermission(Permissions.Staff.PasswordReset);
        }
    }

    private void EnsureCanChangeClinicActor()
    {
        if (_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return;
        }

        if (_currentStaff.HasActiveMembership && _currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            return;
        }

        throw StaffManagementException.ClinicChangeNotAllowed(
            "Only Organization Admin or Platform Admin may reassign staff clinics.");
    }

    private async Task<Domain.Clinics.Clinic> ResolveClinicInOrganizationAsync(
        Guid clinicId,
        Guid organizationId,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return await _dbContext.Clinics
                .Include(c => c.Organization)
                .SingleOrDefaultAsync(c => c.Id == clinicId && c.OrganizationId == organizationId, cancellationToken)
                ?? throw StaffManagementException.NotFound();
        }

        return await _dbContext.Clinics
            .Include(c => c.Organization)
            .SingleOrDefaultAsync(
                c => c.Id == clinicId
                     && c.OrganizationId == organizationId
                     && c.OrganizationId == _currentStaff.OrganizationId,
                cancellationToken)
            ?? throw StaffManagementException.NotFound();
    }

    private async Task EnsureNotLastAdminAsync(StaffMember staff, CancellationToken cancellationToken)
    {
        if (!AdminRoles.Contains(staff.Role) || !staff.IsActive)
        {
            return;
        }

        if (staff.Role == AppRoles.ClinicAdmin)
        {
            var count = await _dbContext.StaffMembers.CountAsync(
                s => s.ClinicId == staff.ClinicId
                     && s.IsActive
                     && s.Role == AppRoles.ClinicAdmin
                     && s.Id != staff.Id,
                cancellationToken);
            if (count == 0)
            {
                throw StaffManagementException.LastAdminProtected();
            }
        }

        if (staff.Role == AppRoles.OrganizationAdmin)
        {
            var count = await _dbContext.StaffMembers.CountAsync(
                s => s.OrganizationId == staff.OrganizationId
                     && s.IsActive
                     && s.Role == AppRoles.OrganizationAdmin
                     && s.Id != staff.Id,
                cancellationToken);
            if (count == 0)
            {
                throw StaffManagementException.LastAdminProtected();
            }
        }
    }

    private static void EnsureExpectedVersion(StaffMember staff, int expectedVersion)
    {
        if (staff.Version != expectedVersion)
        {
            throw StaffManagementException.ConcurrencyConflict();
        }
    }

    private static bool IsAssignableStaffRole(string role) =>
        AppRoles.All.Contains(role, StringComparer.Ordinal) && role != AppRoles.Patient;

    private async Task<StaffDetailResponse> MapDetailAsync(
        StaffMember staff,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var clinicName = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == staff.ClinicId)
            .Select(c => c.Name)
            .SingleOrDefaultAsync(cancellationToken);
        return MapDetail(staff, user, clinicName);
    }

    private static StaffDetailResponse MapDetail(StaffMember staff, ApplicationUser user, string? clinicName) =>
        new()
        {
            StaffMemberId = staff.Id,
            UserId = staff.UserId,
            Email = user.Email ?? string.Empty,
            FirstName = staff.FirstName,
            LastName = staff.LastName,
            DisplayName = staff.DisplayName,
            JobTitle = staff.JobTitle,
            PhoneNumber = user.PhoneNumber,
            OrganizationId = staff.OrganizationId,
            ClinicId = staff.ClinicId,
            ClinicName = clinicName,
            Role = staff.Role,
            MembershipIsActive = staff.IsActive,
            AccountIsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAtUtc = staff.CreatedAtUtc,
            UpdatedAtUtc = staff.UpdatedAtUtc,
            Version = staff.Version,
        };

    private sealed record StaffScope(Guid OrganizationId, Guid? ClinicId, bool OrgWide);
}
