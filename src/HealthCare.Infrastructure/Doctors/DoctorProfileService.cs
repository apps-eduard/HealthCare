using HealthCare.Application.Authorization;
using HealthCare.Application.Doctors;
using HealthCare.Contracts.Doctors;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Doctors;

/// <summary>
/// Narrow Doctor self-profile service. Updates only approved StaffMember + Identity phone fields.
/// Does not expose role, clinic, activation, email, or specialty mutation.
/// Successful reads are not persisted as audit events (same policy as clinic/doctor dashboards).
/// </summary>
public sealed class DoctorProfileService : IDoctorProfileService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IPermissionService _permissions;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DoctorProfileService> _logger;

    public DoctorProfileService(
        HealthCareDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IPermissionService permissions,
        IAuthorizationAuditLogger audit,
        TimeProvider timeProvider,
        ILogger<DoctorProfileService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _permissions = permissions;
        _audit = audit;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<DoctorProfileResponse> GetAsync(
        DoctorProfileQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Doctors.ProfileRead);
        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        return await MapAsync(scope.DoctorStaffMemberId, cancellationToken);
    }

    public async Task<DoctorProfileResponse> UpdateAsync(
        UpdateDoctorProfileRequest request,
        DoctorProfileQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthorized(Permissions.Doctors.ProfileUpdate);

        if (!request.HasAnyEditableField)
        {
            throw DoctorProfileException.EmptyUpdate();
        }

        var scope = await ResolveScopeAsync(query, bypass, cancellationToken);
        var staff = await _dbContext.StaffMembers
            .SingleOrDefaultAsync(s => s.Id == scope.DoctorStaffMemberId, cancellationToken)
            ?? throw DoctorProfileException.DoctorNotFound();

        if (staff.ClinicId != scope.ClinicId
            || staff.Role != AppRoles.Doctor
            || !staff.IsActive)
        {
            throw DoctorProfileException.DoctorNotFound();
        }

        if (staff.Version != request.ExpectedVersion)
        {
            throw DoctorProfileException.ConcurrencyConflict();
        }

        var changed = new List<string>();

        if (request.DisplayNameSpecified)
        {
            var displayName = NormalizeOptional(request.DisplayName);
            if (displayName is { Length: > 200 })
            {
                throw DoctorProfileException.InvalidField("Display name must be at most 200 characters.");
            }

            staff.DisplayName = displayName;
            changed.Add(nameof(UpdateDoctorProfileRequest.DisplayName));
        }

        if (request.FirstNameSpecified)
        {
            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                throw DoctorProfileException.EmptyUpdate();
            }

            var firstName = request.FirstName.Trim();
            if (firstName.Length > 100)
            {
                throw DoctorProfileException.InvalidField("First name must be at most 100 characters.");
            }

            staff.FirstName = firstName;
            changed.Add(nameof(UpdateDoctorProfileRequest.FirstName));
        }

        if (request.LastNameSpecified)
        {
            if (string.IsNullOrWhiteSpace(request.LastName))
            {
                throw DoctorProfileException.EmptyUpdate();
            }

            var lastName = request.LastName.Trim();
            if (lastName.Length > 100)
            {
                throw DoctorProfileException.InvalidField("Last name must be at most 100 characters.");
            }

            staff.LastName = lastName;
            changed.Add(nameof(UpdateDoctorProfileRequest.LastName));
        }

        if (request.JobTitleSpecified)
        {
            var jobTitle = NormalizeOptional(request.JobTitle);
            if (jobTitle is { Length: > 150 })
            {
                throw DoctorProfileException.InvalidField("Job title must be at most 150 characters.");
            }

            staff.JobTitle = jobTitle;
            changed.Add(nameof(UpdateDoctorProfileRequest.JobTitle));
        }

        var user = await _userManager.FindByIdAsync(staff.UserId.ToString())
            ?? throw DoctorProfileException.DoctorNotFound();

        if (request.ContactPhoneSpecified)
        {
            var phone = NormalizeOptional(request.ContactPhone);
            if (phone is { Length: > 30 })
            {
                throw DoctorProfileException.InvalidField("Contact phone must be at most 30 characters.");
            }

            user.PhoneNumber = phone;
            user.UpdatedAtUtc = _timeProvider.GetUtcNow();
            changed.Add(nameof(UpdateDoctorProfileRequest.ContactPhone));
        }

        if (changed.Count == 0)
        {
            throw DoctorProfileException.EmptyUpdate();
        }

        staff.Version++;
        staff.UpdatedAtUtc = _timeProvider.GetUtcNow();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DoctorProfileException.ConcurrencyConflict();
        }

        _audit.StaffOperation(
            "doctor_profile_update",
            "succeeded",
            organizationId: staff.OrganizationId,
            clinicId: staff.ClinicId,
            staffMemberId: staff.Id,
            changedFields: changed);

        _logger.LogInformation(
            "Doctor profile updated. ActorUserId={ActorUserId} DoctorStaffMemberId={DoctorStaffMemberId} ChangedFields={ChangedFields}",
            _currentUser.UserId,
            staff.Id,
            string.Join(',', changed));

        return await MapAsync(staff.Id, cancellationToken);
    }

    private async Task<DoctorProfileResponse> MapAsync(Guid doctorStaffMemberId, CancellationToken cancellationToken)
    {
        var row = await (
            from staff in _dbContext.StaffMembers.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on staff.UserId equals user.Id
            join clinic in _dbContext.Clinics.AsNoTracking() on staff.ClinicId equals clinic.Id
            join org in _dbContext.Organizations.AsNoTracking() on staff.OrganizationId equals org.Id
            where staff.Id == doctorStaffMemberId
            select new
            {
                staff.Id,
                staff.OrganizationId,
                OrganizationName = org.Name,
                staff.ClinicId,
                ClinicName = clinic.Name,
                Specialty = clinic.Specialty,
                Email = user.Email ?? string.Empty,
                ContactPhone = user.PhoneNumber,
                staff.Role,
                staff.DisplayName,
                staff.FirstName,
                staff.LastName,
                staff.JobTitle,
                staff.IsActive,
                staff.CreatedAtUtc,
                staff.UpdatedAtUtc,
                staff.Version,
            }).SingleOrDefaultAsync(cancellationToken)
            ?? throw DoctorProfileException.DoctorNotFound();

        if (row.Role != AppRoles.Doctor)
        {
            throw DoctorProfileException.DoctorNotFound();
        }

        return new DoctorProfileResponse
        {
            StaffMemberId = row.Id,
            OrganizationId = row.OrganizationId,
            OrganizationName = row.OrganizationName,
            ClinicId = row.ClinicId,
            ClinicName = row.ClinicName,
            Email = row.Email,
            Role = row.Role,
            DisplayName = row.DisplayName,
            FirstName = row.FirstName,
            LastName = row.LastName,
            JobTitle = row.JobTitle,
            ContactPhone = row.ContactPhone,
            Specialty = row.Specialty,
            IsActive = row.IsActive,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            Version = row.Version,
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
            throw DoctorProfileException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        _permissions.RequirePermission(permission);
    }

    private async Task<DoctorScope> ResolveScopeAsync(
        DoctorProfileQuery query,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (query.ClinicId is null || query.ClinicId == Guid.Empty)
            {
                throw DoctorProfileException.ClinicScopeRequired();
            }

            if (query.DoctorStaffMemberId is null || query.DoctorStaffMemberId == Guid.Empty)
            {
                throw DoctorProfileException.DoctorScopeRequired();
            }

            var clinicOk = await _dbContext.Clinics.AsNoTracking()
                .AnyAsync(c => c.Id == query.ClinicId.Value, cancellationToken);
            if (!clinicOk)
            {
                _audit.CrossTenantDenied(
                    "doctor_profile",
                    DoctorProfileErrorCodes.ClinicNotFound,
                    null,
                    query.ClinicId);
                throw DoctorProfileException.ClinicNotFound();
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
                    "doctor_profile",
                    DoctorProfileErrorCodes.DoctorNotFound,
                    null,
                    query.ClinicId);
                throw DoctorProfileException.DoctorNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("doctor_profile", null, query.ClinicId);
            return new DoctorScope(query.ClinicId.Value, query.DoctorStaffMemberId.Value);
        }

        if (bypass == PlatformAdminBypass.Explicit && !_currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            throw DoctorProfileException.AccessDenied();
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (!string.Equals(_currentStaff.Role, AppRoles.Doctor, StringComparison.Ordinal))
        {
            throw DoctorProfileException.AccessDenied();
        }

        if (query.ClinicId is Guid requestedClinic
            && requestedClinic != Guid.Empty
            && requestedClinic != _currentStaff.ClinicId)
        {
            _audit.CrossTenantDenied(
                "doctor_profile_clinic_override",
                DoctorProfileErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                requestedClinic);
            throw DoctorProfileException.InvalidScope();
        }

        if (query.DoctorStaffMemberId is Guid requestedDoctor
            && requestedDoctor != Guid.Empty
            && requestedDoctor != _currentStaff.StaffMemberId)
        {
            _audit.CrossTenantDenied(
                "doctor_profile_doctor_override",
                DoctorProfileErrorCodes.InvalidScope,
                _currentStaff.OrganizationId,
                _currentStaff.ClinicId);
            throw DoctorProfileException.InvalidScope();
        }

        if (_currentStaff.ClinicId == Guid.Empty || _currentStaff.StaffMemberId == Guid.Empty)
        {
            throw DoctorProfileException.DoctorNotFound();
        }

        return new DoctorScope(_currentStaff.ClinicId, _currentStaff.StaffMemberId);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private sealed record DoctorScope(Guid ClinicId, Guid DoctorStaffMemberId);
}
