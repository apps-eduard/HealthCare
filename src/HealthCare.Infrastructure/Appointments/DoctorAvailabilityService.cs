using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Contracts.Appointments;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

public sealed class DoctorAvailabilityService : IDoctorAvailabilityService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IAuthorizationAuditLogger _audit;
    private readonly ILogger<DoctorAvailabilityService> _logger;

    public DoctorAvailabilityService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IAuthorizationAuditLogger audit,
        ILogger<DoctorAvailabilityService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DoctorAvailabilityResponse>> ListAvailabilityAsync(
        Guid staffMemberId,
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var doctor = await LoadManagedDoctorAsync(staffMemberId, clinicId, bypass, cancellationToken);
        var timeZoneId = await ResolveClinicTimeZoneAsync(doctor.ClinicId, cancellationToken);
        var rows = await _dbContext.DoctorAvailabilities
            .AsNoTracking()
            .Where(a => a.DoctorStaffMemberId == doctor.Id)
            .OrderBy(a => a.DayOfWeek)
            .ThenBy(a => a.StartLocalTime)
            .ToListAsync(cancellationToken);

        _audit.AvailabilityOperation(
            "availability_list",
            "succeeded",
            doctor.OrganizationId,
            doctor.ClinicId,
            doctor.Id);

        return rows.Select(a => MapAvailability(a, timeZoneId)).ToList();
    }

    public async Task<IReadOnlyList<DoctorAvailabilityExceptionResponse>> ListExceptionsAsync(
        Guid staffMemberId,
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var doctor = await LoadManagedDoctorAsync(staffMemberId, clinicId, bypass, cancellationToken);
        var rows = await _dbContext.DoctorAvailabilityExceptions
            .AsNoTracking()
            .Where(e => e.DoctorStaffMemberId == doctor.Id)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.StartLocalTime)
            .ToListAsync(cancellationToken);

        _audit.AvailabilityOperation(
            "availability_exceptions_list",
            "succeeded",
            doctor.OrganizationId,
            doctor.ClinicId,
            doctor.Id);

        return rows.Select(MapException).ToList();
    }

    public async Task<DoctorAvailabilityResponse> CreateAvailabilityAsync(
        Guid staffMemberId,
        CreateDoctorAvailabilityRequest request,
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var doctor = await LoadManagedDoctorAsync(staffMemberId, clinicId, bypass, cancellationToken);
        var day = Enum.Parse<DayOfWeek>(request.DayOfWeek, ignoreCase: true);
        var start = TimeOnly.Parse(request.StartLocalTime);
        var end = TimeOnly.Parse(request.EndLocalTime);

        if (start >= end || !AvailabilitySlotRules.IsValidDuration(request.SlotDurationMinutes))
        {
            throw AvailabilityException.Invalid();
        }

        if (request.EffectiveTo.HasValue && request.EffectiveTo.Value < request.EffectiveFrom)
        {
            throw AvailabilityException.Invalid();
        }

        await EnsureNoOverlappingWindowAsync(
            doctor.Id,
            day,
            start,
            end,
            request.EffectiveFrom,
            request.EffectiveTo,
            excludeId: null,
            cancellationToken);

        var entity = new DoctorAvailability
        {
            Id = Guid.NewGuid(),
            OrganizationId = doctor.OrganizationId,
            ClinicId = doctor.ClinicId,
            DoctorStaffMemberId = doctor.Id,
            DayOfWeek = day,
            StartLocalTime = start,
            EndLocalTime = end,
            SlotDurationMinutes = request.SlotDurationMinutes,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
            IsActive = true,
            Version = 0,
        };

        _dbContext.DoctorAvailabilities.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Availability created. UserId={UserId} DoctorStaffMemberId={DoctorStaffMemberId} AvailabilityId={AvailabilityId}",
            _currentUser.UserId,
            doctor.Id,
            entity.Id);
        _audit.AvailabilityOperation(
            "availability_created",
            "succeeded",
            doctor.OrganizationId,
            doctor.ClinicId,
            doctor.Id);

        var timeZoneId = await ResolveClinicTimeZoneAsync(doctor.ClinicId, cancellationToken);
        return MapAvailability(entity, timeZoneId);
    }

    public async Task<DoctorAvailabilityResponse> UpdateAvailabilityAsync(
        Guid staffMemberId,
        Guid availabilityId,
        UpdateDoctorAvailabilityRequest request,
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var doctor = await LoadManagedDoctorAsync(staffMemberId, clinicId, bypass, cancellationToken);
        var entity = await _dbContext.DoctorAvailabilities
            .SingleOrDefaultAsync(a => a.Id == availabilityId && a.DoctorStaffMemberId == doctor.Id, cancellationToken)
            ?? throw AvailabilityException.DoctorNotFound();

        if (entity.Version != request.ExpectedVersion)
        {
            throw AvailabilityException.Concurrency();
        }

        if (request.StartLocalTime is not null)
        {
            entity.StartLocalTime = TimeOnly.Parse(request.StartLocalTime);
        }

        if (request.EndLocalTime is not null)
        {
            entity.EndLocalTime = TimeOnly.Parse(request.EndLocalTime);
        }

        if (request.SlotDurationMinutes.HasValue)
        {
            entity.SlotDurationMinutes = request.SlotDurationMinutes.Value;
        }

        if (request.EffectiveFrom.HasValue)
        {
            entity.EffectiveFrom = request.EffectiveFrom.Value;
        }

        if (request.ClearEffectiveTo == true)
        {
            entity.EffectiveTo = null;
        }
        else if (request.EffectiveTo.HasValue)
        {
            entity.EffectiveTo = request.EffectiveTo.Value;
        }

        if (request.IsActive.HasValue)
        {
            entity.IsActive = request.IsActive.Value;
        }

        if (entity.StartLocalTime >= entity.EndLocalTime
            || !AvailabilitySlotRules.IsValidDuration(entity.SlotDurationMinutes)
            || (entity.EffectiveTo.HasValue && entity.EffectiveTo.Value < entity.EffectiveFrom))
        {
            throw AvailabilityException.Invalid();
        }

        await EnsureNoOverlappingWindowAsync(
            doctor.Id,
            entity.DayOfWeek,
            entity.StartLocalTime,
            entity.EndLocalTime,
            entity.EffectiveFrom,
            entity.EffectiveTo,
            entity.Id,
            cancellationToken);

        entity.Version++;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AvailabilityException.Concurrency();
        }

        _logger.LogInformation(
            "Availability updated. UserId={UserId} AvailabilityId={AvailabilityId} Version={Version}",
            _currentUser.UserId,
            entity.Id,
            entity.Version);
        _audit.AvailabilityOperation(
            "availability_updated",
            "succeeded",
            doctor.OrganizationId,
            doctor.ClinicId,
            doctor.Id);

        var timeZoneId = await ResolveClinicTimeZoneAsync(doctor.ClinicId, cancellationToken);
        return MapAvailability(entity, timeZoneId);
    }

    public async Task DeleteAvailabilityAsync(
        Guid staffMemberId,
        Guid availabilityId,
        int expectedVersion,
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var doctor = await LoadManagedDoctorAsync(staffMemberId, clinicId, bypass, cancellationToken);
        var entity = await _dbContext.DoctorAvailabilities
            .SingleOrDefaultAsync(a => a.Id == availabilityId && a.DoctorStaffMemberId == doctor.Id, cancellationToken)
            ?? throw AvailabilityException.DoctorNotFound();

        if (entity.Version != expectedVersion)
        {
            throw AvailabilityException.Concurrency();
        }

        _dbContext.DoctorAvailabilities.Remove(entity);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AvailabilityException.Concurrency();
        }

        _logger.LogInformation(
            "Availability removed. UserId={UserId} AvailabilityId={AvailabilityId}",
            _currentUser.UserId,
            availabilityId);
        _audit.AvailabilityOperation(
            "availability_deleted",
            "succeeded",
            doctor.OrganizationId,
            doctor.ClinicId,
            doctor.Id);
    }

    public async Task<DoctorAvailabilityExceptionResponse> CreateExceptionAsync(
        Guid staffMemberId,
        CreateDoctorAvailabilityExceptionRequest request,
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var doctor = await LoadManagedDoctorAsync(staffMemberId, clinicId, bypass, cancellationToken);
        var type = Enum.Parse<AvailabilityExceptionType>(request.ExceptionType, ignoreCase: true);

        TimeOnly? start = null;
        TimeOnly? end = null;
        if (type != AvailabilityExceptionType.UnavailableFullDay)
        {
            start = TimeOnly.Parse(request.StartLocalTime!);
            end = TimeOnly.Parse(request.EndLocalTime!);
            if (start >= end)
            {
                throw AvailabilityException.Invalid();
            }
        }

        var entity = new DoctorAvailabilityException
        {
            Id = Guid.NewGuid(),
            OrganizationId = doctor.OrganizationId,
            ClinicId = doctor.ClinicId,
            DoctorStaffMemberId = doctor.Id,
            Date = request.Date,
            ExceptionType = type,
            StartLocalTime = start,
            EndLocalTime = end,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            Version = 0,
        };

        _dbContext.DoctorAvailabilityExceptions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Availability exception created. UserId={UserId} DoctorStaffMemberId={DoctorStaffMemberId} ExceptionId={ExceptionId}",
            _currentUser.UserId,
            doctor.Id,
            entity.Id);
        _audit.AvailabilityOperation(
            "availability_exception_created",
            "succeeded",
            doctor.OrganizationId,
            doctor.ClinicId,
            doctor.Id);

        return MapException(entity);
    }

    public async Task DeleteExceptionAsync(
        Guid staffMemberId,
        Guid exceptionId,
        int expectedVersion,
        Guid? clinicId = null,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var doctor = await LoadManagedDoctorAsync(staffMemberId, clinicId, bypass, cancellationToken);
        var entity = await _dbContext.DoctorAvailabilityExceptions
            .SingleOrDefaultAsync(e => e.Id == exceptionId && e.DoctorStaffMemberId == doctor.Id, cancellationToken)
            ?? throw AvailabilityException.DoctorNotFound();

        if (entity.Version != expectedVersion)
        {
            throw AvailabilityException.Concurrency();
        }

        _dbContext.DoctorAvailabilityExceptions.Remove(entity);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AvailabilityException.Concurrency();
        }

        _logger.LogInformation(
            "Availability exception removed. UserId={UserId} ExceptionId={ExceptionId}",
            _currentUser.UserId,
            exceptionId);
        _audit.AvailabilityOperation(
            "availability_exception_deleted",
            "succeeded",
            doctor.OrganizationId,
            doctor.ClinicId,
            doctor.Id);
    }

    private async Task EnsureNoOverlappingWindowAsync(
        Guid doctorId,
        DayOfWeek day,
        TimeOnly start,
        TimeOnly end,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.DoctorAvailabilities
            .AsNoTracking()
            .Where(a => a.DoctorStaffMemberId == doctorId
                        && a.DayOfWeek == day
                        && a.IsActive
                        && (!excludeId.HasValue || a.Id != excludeId.Value))
            .ToListAsync(cancellationToken);

        foreach (var row in existing)
        {
            if (!AvailabilitySlotRules.DateRangesOverlap(
                    effectiveFrom, effectiveTo, row.EffectiveFrom, row.EffectiveTo))
            {
                continue;
            }

            if (AvailabilitySlotRules.TimeRangesOverlap(start, end, row.StartLocalTime, row.EndLocalTime))
            {
                throw AvailabilityException.Conflict();
            }
        }
    }

    private async Task<StaffMember> LoadManagedDoctorAsync(
        Guid staffMemberId,
        Guid? clinicId,
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

        var doctor = await _dbContext.StaffMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == staffMemberId, cancellationToken);

        if (doctor is null || !doctor.IsActive || doctor.Role != AppRoles.Doctor)
        {
            throw AvailabilityException.DoctorNotFound();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            if (clinicId.HasValue && doctor.ClinicId != clinicId.Value)
            {
                _audit.CrossTenantDenied(
                    "availability_clinic_mismatch",
                    Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                    doctor.OrganizationId,
                    clinicId);
                throw AvailabilityException.DoctorNotFound();
            }

            _audit.ExplicitPlatformBypassUsed("availability_manage", doctor.OrganizationId, doctor.ClinicId);
            return doctor;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (clinicId.HasValue && doctor.ClinicId != clinicId.Value)
        {
            _audit.CrossTenantDenied(
                "availability_clinic_mismatch",
                Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                _currentStaff.OrganizationId,
                clinicId);
            throw AvailabilityException.DoctorNotFound();
        }

        var role = _currentStaff.Role;
        if (role == AppRoles.OrganizationAdmin)
        {
            if (doctor.OrganizationId != _currentStaff.OrganizationId)
            {
                _audit.CrossTenantDenied(
                    "availability_cross_org_denied",
                    Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                    _currentStaff.OrganizationId,
                    doctor.ClinicId);
                _logger.LogInformation(
                    "Cross-tenant availability access denied. UserId={UserId} DoctorStaffMemberId={DoctorStaffMemberId}",
                    _currentUser.UserId,
                    staffMemberId);
                throw AvailabilityException.DoctorNotFound();
            }

            if (clinicId.HasValue)
            {
                var clinic = await _dbContext.Clinics
                    .AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Id == clinicId.Value, cancellationToken);
                if (clinic is null || clinic.OrganizationId != _currentStaff.OrganizationId)
                {
                    _audit.CrossTenantDenied(
                        "availability_clinic_filter_denied",
                        Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                        _currentStaff.OrganizationId,
                        clinicId);
                    throw AuthorizationException.ClinicAccessDenied();
                }
            }

            return doctor;
        }

        if (role == AppRoles.ClinicAdmin)
        {
            if (doctor.ClinicId != _currentStaff.ClinicId)
            {
                _audit.CrossTenantDenied(
                    "availability_cross_clinic_denied",
                    Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                    _currentStaff.OrganizationId,
                    doctor.ClinicId);
                throw AvailabilityException.DoctorNotFound();
            }

            return doctor;
        }

        if (role == AppRoles.Doctor && _currentStaff.StaffMemberId == doctor.Id)
        {
            return doctor;
        }

        throw AuthorizationException.Forbidden();
    }

    private async Task<string?> ResolveClinicTimeZoneAsync(Guid clinicId, CancellationToken cancellationToken) =>
        await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == clinicId)
            .Select(c => c.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);

    private static DoctorAvailabilityResponse MapAvailability(DoctorAvailability a, string? timeZoneId) =>
        new()
        {
            Id = a.Id,
            ClinicId = a.ClinicId,
            DoctorStaffMemberId = a.DoctorStaffMemberId,
            DayOfWeek = a.DayOfWeek.ToString(),
            StartLocalTime = a.StartLocalTime.ToString("HH:mm"),
            EndLocalTime = a.EndLocalTime.ToString("HH:mm"),
            SlotDurationMinutes = a.SlotDurationMinutes,
            EffectiveFrom = a.EffectiveFrom,
            EffectiveTo = a.EffectiveTo,
            IsActive = a.IsActive,
            Version = a.Version,
            ClinicTimeZoneId = timeZoneId,
        };

    private static DoctorAvailabilityExceptionResponse MapException(DoctorAvailabilityException e) =>
        new()
        {
            Id = e.Id,
            DoctorStaffMemberId = e.DoctorStaffMemberId,
            Date = e.Date,
            ExceptionType = e.ExceptionType.ToString(),
            StartLocalTime = e.StartLocalTime?.ToString("HH:mm"),
            EndLocalTime = e.EndLocalTime?.ToString("HH:mm"),
            Reason = e.Reason,
            Version = e.Version,
        };
}
