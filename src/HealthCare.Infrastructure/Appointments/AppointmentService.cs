using HealthCare.Application.Appointments;
using HealthCare.Application.Authorization;
using HealthCare.Application.Patients;
using HealthCare.Contracts.Appointments;
using HealthCare.Contracts.Common;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Appointments;

/// <summary>
/// Appointment booking and status workflow.
/// Tenant scope is enforced explicitly (EF global filters remain deferred).
/// Assigned staff for appointments: DOCTOR only (MVP assumption).
/// Patient-created appointments start as Requested; staff-created as Confirmed.
/// </summary>
public sealed class AppointmentService : IAppointmentService
{
    public static readonly HashSet<string> AssignableRoles = new(StringComparer.Ordinal)
    {
        AppRoles.Doctor,
    };

    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly ICurrentPatient _currentPatient;
    private readonly IClinicPublicLookup _clinicLookup;
    private readonly IAppointmentSlotService _slots;
    private readonly IAppointmentReminderScheduler _reminders;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentService> _logger;

    public AppointmentService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        ICurrentPatient currentPatient,
        IClinicPublicLookup clinicLookup,
        IAppointmentSlotService slots,
        IAppointmentReminderScheduler reminders,
        TimeProvider timeProvider,
        ILogger<AppointmentService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _currentPatient = currentPatient;
        _clinicLookup = clinicLookup;
        _slots = slots;
        _reminders = reminders;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AppointmentResponse> CreateForCurrentPatientAsync(
        CreatePatientAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedPatient();

        var patientId = _currentPatient.PatientId!.Value;
        var patient = await _dbContext.Patients
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == patientId, cancellationToken);

        if (patient is null || !patient.IsActive)
        {
            throw AppointmentException.InactivePatient();
        }

        var clinic = await _clinicLookup.FindByPublicCodeAsync(request.ClinicCode.Trim(), cancellationToken);
        if (clinic is null || !clinic.IsActive)
        {
            throw AppointmentException.InactiveClinic();
        }

        var organization = await _dbContext.Organizations
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == clinic.OrganizationId, cancellationToken);

        if (organization is null || organization.Status != OrganizationStatus.Active)
        {
            throw AppointmentException.InactiveClinic();
        }

        var enrollment = await _dbContext.ClinicPatients
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cp => cp.ClinicId == clinic.Id
                      && cp.PatientId == patientId
                      && cp.Status == ClinicPatientStatus.Active,
                cancellationToken);

        if (enrollment is null)
        {
            throw AppointmentException.NotEnrolled();
        }

        await EnsureAssignableDoctorAsync(request.DoctorStaffMemberId, clinic.Id, cancellationToken);
        EnsureFutureStart(request.AppointmentDateUtc);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = clinic.OrganizationId,
            ClinicId = clinic.Id,
            PatientId = patientId,
            ClinicPatientId = enrollment.Id,
            DoctorStaffMemberId = request.DoctorStaffMemberId,
            AppointmentDateUtc = request.AppointmentDateUtc,
            DurationMinutes = request.DurationMinutes,
            Reason = NormalizeOptional(request.Reason),
            PatientNotes = NormalizeOptional(request.PatientNotes),
            Status = AppointmentStatus.Requested,
            Source = AppointmentSource.Patient,
            CreatedByUserId = _currentUser.UserId!.Value,
            Version = 0,
        };

        await PersistNewAppointmentAsync(appointment, "appointment_requested", cancellationToken);
        await _reminders.ScheduleAfterAppointmentCreatedAsync(appointment.Id, cancellationToken);
        return Map(appointment);
    }

    public async Task<AppointmentResponse> CreateForStaffAsync(
        CreateStaffAppointmentRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveStaffClinicScopeAsync(requestClinicId: null, bypass, cancellationToken);
        EnsureFutureStart(request.AppointmentDateUtc);

        var patient = await _dbContext.Patients
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken);

        if (patient is null || !patient.IsActive)
        {
            throw AppointmentException.InactivePatient();
        }

        var clinicId = scope.ClinicId
            ?? throw AuthorizationException.ClinicAccessDenied();

        var enrollment = await _dbContext.ClinicPatients
            .AsNoTracking()
            .SingleOrDefaultAsync(
                cp => cp.ClinicId == clinicId
                      && cp.PatientId == request.PatientId
                      && cp.Status == ClinicPatientStatus.Active,
                cancellationToken);

        if (enrollment is null)
        {
            LogDenied("appointment_staff_create_not_enrolled", request.PatientId);
            throw AppointmentException.NotEnrolled();
        }

        await EnsureAssignableDoctorAsync(request.DoctorStaffMemberId, clinicId, cancellationToken);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            OrganizationId = scope.OrganizationId,
            ClinicId = clinicId,
            PatientId = request.PatientId,
            ClinicPatientId = enrollment.Id,
            DoctorStaffMemberId = request.DoctorStaffMemberId,
            AppointmentDateUtc = request.AppointmentDateUtc,
            DurationMinutes = request.DurationMinutes,
            Reason = NormalizeOptional(request.Reason),
            PatientNotes = NormalizeOptional(request.PatientNotes),
            Status = AppointmentStatus.Confirmed,
            Source = AppointmentSource.Staff,
            CreatedByUserId = _currentUser.UserId!.Value,
            Version = 0,
        };

        await PersistNewAppointmentAsync(appointment, "appointment_created_by_staff", cancellationToken);
        await _reminders.ScheduleAfterAppointmentCreatedAsync(appointment.Id, cancellationToken);
        return Map(appointment);
    }

    public async Task<PagedResponse<AppointmentResponse>> ListForCurrentPatientAsync(
        AppointmentListQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureAuthenticatedPatient();
        var patientId = _currentPatient.PatientId!.Value;

        var appointments = ApplyListFilters(
            _dbContext.Appointments.AsNoTracking().Where(a => a.PatientId == patientId),
            query);

        return await PageAsync(appointments, query, cancellationToken);
    }

    public async Task<PagedResponse<AppointmentResponse>> ListForStaffAsync(
        AppointmentListQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveStaffListScopeAsync(query.ClinicId, bypass, cancellationToken);

        IQueryable<Appointment> appointments = _dbContext.Appointments.AsNoTracking();
        if (scope.Mode == StaffScopeMode.Clinic)
        {
            appointments = appointments.Where(a => a.ClinicId == scope.ClinicId);
        }
        else if (scope.Mode == StaffScopeMode.Organization)
        {
            appointments = appointments.Where(a => a.OrganizationId == scope.OrganizationId);
            if (scope.ClinicId.HasValue)
            {
                appointments = appointments.Where(a => a.ClinicId == scope.ClinicId.Value);
            }
        }
        else
        {
            appointments = appointments.Where(a => a.ClinicId == scope.ClinicId);
        }

        appointments = ApplyListFilters(appointments, query);
        return await PageAsync(appointments, query, cancellationToken);
    }

    public async Task<AppointmentResponse> GetByIdAsync(
        Guid appointmentId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAccessibleAsync(appointmentId, asNoTracking: true, bypass, cancellationToken);
        return Map(appointment);
    }

    public Task<AppointmentResponse> ConfirmAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default) =>
        TransitionStaffAsync(appointmentId, request, AppointmentStatus.Confirmed, "appointment_confirmed", bypass, cancellationToken);

    public async Task<AppointmentResponse> CancelAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        var appointment = await LoadAccessibleAsync(appointmentId, asNoTracking: false, bypass, cancellationToken);
        EnsureExpectedVersion(appointment, request.ExpectedVersion);

        AppointmentStatus target;
        if (_currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership)
        {
            if (appointment.AppointmentDateUtc <= _timeProvider.GetUtcNow())
            {
                throw AppointmentException.InvalidTime();
            }

            target = AppointmentStatus.CancelledByPatient;
        }
        else
        {
            EnsureStaffCanMutate(appointment, bypass);
            target = AppointmentStatus.CancelledByClinic;
        }

        return await ApplyTransitionAsync(
            appointment,
            target,
            request.CancellationReason,
            "appointment_cancelled",
            cancellationToken);
    }

    public Task<AppointmentResponse> CheckInAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default) =>
        TransitionStaffAsync(appointmentId, request, AppointmentStatus.CheckedIn, "appointment_checked_in", bypass, cancellationToken);

    public Task<AppointmentResponse> CompleteAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default) =>
        TransitionStaffAsync(appointmentId, request, AppointmentStatus.Completed, "appointment_completed", bypass, cancellationToken);

    public Task<AppointmentResponse> MarkNoShowAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default) =>
        TransitionStaffAsync(appointmentId, request, AppointmentStatus.NoShow, "appointment_no_show", bypass, cancellationToken);

    private async Task<AppointmentResponse> TransitionStaffAsync(
        Guid appointmentId,
        AppointmentActionRequest request,
        AppointmentStatus target,
        string operation,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        var appointment = await LoadAccessibleAsync(appointmentId, asNoTracking: false, bypass, cancellationToken);
        EnsureStaffCanMutate(appointment, bypass);
        EnsureExpectedVersion(appointment, request.ExpectedVersion);
        return await ApplyTransitionAsync(appointment, target, request.CancellationReason, operation, cancellationToken);
    }

    private async Task<AppointmentResponse> ApplyTransitionAsync(
        Appointment appointment,
        AppointmentStatus target,
        string? cancellationReason,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!AppointmentStatusTransitions.CanTransition(appointment.Status, target))
        {
            throw AppointmentException.InvalidTransition();
        }

        appointment.Status = target;
        if (AppointmentStatusTransitions.IsCancelled(target))
        {
            appointment.CancellationReason = NormalizeOptional(cancellationReason);
        }

        appointment.Version++;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw AppointmentException.ConcurrencyConflict();
        }

        _logger.LogInformation(
            "Appointment status changed. UserId={UserId} AppointmentId={AppointmentId} Status={Status} Operation={Operation}",
            _currentUser.UserId,
            appointment.Id,
            appointment.Status,
            operation);

        if (target == AppointmentStatus.Confirmed)
        {
            await _reminders.ScheduleAfterAppointmentConfirmedAsync(appointment.Id, cancellationToken);
        }
        else if (AppointmentStatusTransitions.IsCancelled(target))
        {
            await _reminders.ScheduleAfterAppointmentCancelledAsync(appointment.Id, cancellationToken);
        }

        return Map(appointment);
    }

    private async Task PersistNewAppointmentAsync(
        Appointment appointment,
        string operation,
        CancellationToken cancellationToken)
    {
        var useTransaction = _dbContext.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (useTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable,
                cancellationToken);
        }

        try
        {
            await _slots.EnsureSlotIsBookableAsync(
                appointment.ClinicId,
                appointment.DoctorStaffMemberId,
                appointment.AppointmentDateUtc,
                appointment.DurationMinutes,
                excludeAppointmentId: null,
                cancellationToken);

            if (await HasSlotConflictAsync(
                    appointment.ClinicId,
                    appointment.DoctorStaffMemberId,
                    appointment.AppointmentDateUtc,
                    appointment.DurationMinutes,
                    excludeAppointmentId: null,
                    cancellationToken))
            {
                _logger.LogInformation(
                    "Appointment slot conflict. UserId={UserId} ClinicId={ClinicId} DoctorStaffMemberId={DoctorStaffMemberId} Operation={Operation}",
                    _currentUser.UserId,
                    appointment.ClinicId,
                    appointment.DoctorStaffMemberId,
                    "appointment_slot_conflict");
                throw AppointmentException.SlotConflict();
            }

            _dbContext.Appointments.Add(appointment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        _logger.LogInformation(
            "Appointment created. UserId={UserId} AppointmentId={AppointmentId} ClinicId={ClinicId} PatientId={PatientId} Operation={Operation}",
            _currentUser.UserId,
            appointment.Id,
            appointment.ClinicId,
            appointment.PatientId,
            operation);
    }

    private async Task<bool> HasSlotConflictAsync(
        Guid clinicId,
        Guid doctorStaffMemberId,
        DateTimeOffset start,
        int durationMinutes,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        var end = start.AddMinutes(durationMinutes);

        // Load candidate windows for the doctor/clinic and evaluate overlap in memory.
        // Cancelled appointments do not block slots.
        var candidates = await _dbContext.Appointments
            .AsNoTracking()
            .Where(a => a.ClinicId == clinicId
                        && a.DoctorStaffMemberId == doctorStaffMemberId
                        && a.Status != AppointmentStatus.CancelledByPatient
                        && a.Status != AppointmentStatus.CancelledByClinic
                        && (!excludeAppointmentId.HasValue || a.Id != excludeAppointmentId.Value)
                        && a.AppointmentDateUtc < end)
            .Select(a => new { a.AppointmentDateUtc, a.DurationMinutes })
            .ToListAsync(cancellationToken);

        return candidates.Any(a => a.AppointmentDateUtc.AddMinutes(a.DurationMinutes) > start);
    }

    private async Task EnsureAssignableDoctorAsync(
        Guid staffMemberId,
        Guid clinicId,
        CancellationToken cancellationToken)
    {
        var staff = await _dbContext.StaffMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == staffMemberId, cancellationToken);

        if (staff is null
            || !staff.IsActive
            || staff.ClinicId != clinicId
            || !AssignableRoles.Contains(staff.Role))
        {
            throw AppointmentException.InvalidAssignedStaff();
        }
    }

    private void EnsureFutureStart(DateTimeOffset start)
    {
        if (start <= _timeProvider.GetUtcNow())
        {
            throw AppointmentException.InvalidTime();
        }
    }

    private void EnsureAuthenticatedPatient()
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.IsInRole(AppRoles.Patient))
        {
            throw AuthorizationException.Forbidden();
        }

        if (!_currentPatient.HasLinkedPatient || _currentPatient.PatientId is null)
        {
            throw AuthorizationException.MissingPatientLinkage();
        }
    }

    private void EnsureExpectedVersion(Appointment appointment, int expectedVersion)
    {
        if (appointment.Version != expectedVersion)
        {
            throw AppointmentException.ConcurrencyConflict();
        }
    }

    private void EnsureStaffCanMutate(Appointment appointment, PlatformAdminBypass bypass)
    {
        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            return;
        }

        if (!_currentStaff.HasActiveMembership)
        {
            throw AuthorizationException.MissingStaffMembership();
        }

        if (_currentStaff.Role == AppRoles.OrganizationAdmin)
        {
            if (appointment.OrganizationId != _currentStaff.OrganizationId)
            {
                LogDenied("appointment_org_mutate_denied", appointment.Id);
                throw AppointmentException.NotFoundOrDenied();
            }

            return;
        }

        if (appointment.ClinicId != _currentStaff.ClinicId)
        {
            LogDenied("appointment_clinic_mutate_denied", appointment.Id);
            throw AppointmentException.NotFoundOrDenied();
        }
    }

    private async Task<Appointment> LoadAccessibleAsync(
        Guid appointmentId,
        bool asNoTracking,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        IQueryable<Appointment> query = _dbContext.Appointments;
        if (asNoTracking)
        {
            query = query.AsNoTracking();
        }

        var appointment = await query.SingleOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
        if (appointment is null)
        {
            throw AppointmentException.NotFoundOrDenied();
        }

        if (bypass == PlatformAdminBypass.Explicit && _currentUser.IsInRole(AppRoles.PlatformAdmin))
        {
            _logger.LogInformation(
                "PLATFORM_ADMIN explicit appointment bypass. UserId={UserId} AppointmentId={AppointmentId}",
                _currentUser.UserId,
                appointmentId);
            return appointment;
        }

        if (_currentUser.IsInRole(AppRoles.Patient)
            && _currentPatient.HasLinkedPatient
            && _currentPatient.PatientId == appointment.PatientId)
        {
            return appointment;
        }

        if (_currentStaff.HasActiveMembership)
        {
            if (_currentStaff.Role == AppRoles.OrganizationAdmin
                && appointment.OrganizationId == _currentStaff.OrganizationId)
            {
                return appointment;
            }

            if (appointment.ClinicId == _currentStaff.ClinicId)
            {
                return appointment;
            }
        }

        LogDenied("appointment_access_denied", appointmentId);
        throw AppointmentException.NotFoundOrDenied();
    }

    private async Task<StaffScope> ResolveStaffClinicScopeAsync(
        Guid? requestClinicId,
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
                // Staff create is clinic-scoped: org admin must supply a clinic in their org via trusted staff clinic
                // MVP: use their assigned clinic membership ClinicId.
                return StaffScope.ForClinic(_currentStaff.OrganizationId, _currentStaff.ClinicId);
            }

            if (requestClinicId.HasValue && requestClinicId.Value != _currentStaff.ClinicId)
            {
                _logger.LogInformation(
                    "Client ClinicId ignored for staff appointment create. UserId={UserId} TrustedClinicId={ClinicId}",
                    _currentUser.UserId,
                    _currentStaff.ClinicId);
            }

            return StaffScope.ForClinic(_currentStaff.OrganizationId, _currentStaff.ClinicId);
        }

        if (bypass == PlatformAdminBypass.Explicit
            && _currentUser.IsInRole(AppRoles.PlatformAdmin)
            && requestClinicId.HasValue)
        {
            var clinic = await _dbContext.Clinics
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == requestClinicId.Value, cancellationToken);
            if (clinic is null)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            return StaffScope.ForPlatform(clinic.OrganizationId, clinic.Id);
        }

        throw AuthorizationException.MissingStaffMembership();
    }

    private async Task<StaffScope> ResolveStaffListScopeAsync(
        Guid? clinicIdFilter,
        PlatformAdminBypass bypass,
        CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
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
                        throw AuthorizationException.ClinicAccessDenied();
                    }

                    clinicId = clinic.Id;
                }

                return StaffScope.ForOrganization(_currentStaff.OrganizationId, clinicId);
            }

            return StaffScope.ForClinic(_currentStaff.OrganizationId, _currentStaff.ClinicId);
        }

        if (bypass == PlatformAdminBypass.Explicit
            && _currentUser.IsInRole(AppRoles.PlatformAdmin)
            && clinicIdFilter.HasValue)
        {
            var clinic = await _dbContext.Clinics
                .AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == clinicIdFilter.Value, cancellationToken);
            if (clinic is null)
            {
                throw AuthorizationException.ClinicAccessDenied();
            }

            return StaffScope.ForPlatform(clinic.OrganizationId, clinic.Id);
        }

        throw AuthorizationException.MissingStaffMembership();
    }

    private static IQueryable<Appointment> ApplyListFilters(
        IQueryable<Appointment> query,
        AppointmentListQuery listQuery)
    {
        if (listQuery.FromUtc.HasValue)
        {
            query = query.Where(a => a.AppointmentDateUtc >= listQuery.FromUtc.Value);
        }

        if (listQuery.ToUtc.HasValue)
        {
            query = query.Where(a => a.AppointmentDateUtc <= listQuery.ToUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(listQuery.Status)
            && Enum.TryParse<AppointmentStatus>(listQuery.Status, ignoreCase: true, out var status))
        {
            query = query.Where(a => a.Status == status);
        }

        if (listQuery.DoctorStaffMemberId.HasValue)
        {
            query = query.Where(a => a.DoctorStaffMemberId == listQuery.DoctorStaffMemberId.Value);
        }

        return query;
    }

    private static async Task<PagedResponse<AppointmentResponse>> PageAsync(
        IQueryable<Appointment> query,
        AppointmentListQuery listQuery,
        CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var desc = listQuery.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        var sortBy = listQuery.SortBy.ToLowerInvariant();

        query = sortBy switch
        {
            "status" => desc
                ? query.OrderByDescending(a => a.Status).ThenBy(a => a.Id)
                : query.OrderBy(a => a.Status).ThenBy(a => a.Id),
            "createdatutc" => desc
                ? query.OrderByDescending(a => a.CreatedAtUtc).ThenBy(a => a.Id)
                : query.OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id),
            "durationminutes" => desc
                ? query.OrderByDescending(a => a.DurationMinutes).ThenBy(a => a.Id)
                : query.OrderBy(a => a.DurationMinutes).ThenBy(a => a.Id),
            _ => desc
                ? query.OrderByDescending(a => a.AppointmentDateUtc).ThenBy(a => a.Id)
                : query.OrderBy(a => a.AppointmentDateUtc).ThenBy(a => a.Id),
        };

        var page = listQuery.Page < 1 ? 1 : listQuery.Page;
        var pageSize = listQuery.PageSize < 1
            ? AppointmentListQueryValidator.DefaultPageSize
            : Math.Min(listQuery.PageSize, AppointmentListQueryValidator.MaxPageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AppointmentResponse
            {
                Id = a.Id,
                OrganizationId = a.OrganizationId,
                ClinicId = a.ClinicId,
                PatientId = a.PatientId,
                ClinicPatientId = a.ClinicPatientId,
                DoctorStaffMemberId = a.DoctorStaffMemberId,
                AppointmentDateUtc = a.AppointmentDateUtc,
                DurationMinutes = a.DurationMinutes,
                EndsAtUtc = a.AppointmentDateUtc.AddMinutes(a.DurationMinutes),
                Reason = a.Reason,
                Status = a.Status.ToString(),
                PatientNotes = a.PatientNotes,
                CancellationReason = a.CancellationReason,
                Source = a.Source.ToString(),
                Version = a.Version,
                CreatedAtUtc = a.CreatedAtUtc,
                UpdatedAtUtc = a.UpdatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        return PagedResponse<AppointmentResponse>.Create(items, page, pageSize, totalCount);
    }

    private void LogDenied(string reason, Guid resourceId)
    {
        _logger.LogInformation(
            "Appointment access denied. UserId={UserId} Reason={ReasonCode} Resource={ResourceKey}",
            _currentUser.UserId,
            reason,
            resourceId);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static AppointmentResponse Map(Appointment a) =>
        new()
        {
            Id = a.Id,
            OrganizationId = a.OrganizationId,
            ClinicId = a.ClinicId,
            PatientId = a.PatientId,
            ClinicPatientId = a.ClinicPatientId,
            DoctorStaffMemberId = a.DoctorStaffMemberId,
            AppointmentDateUtc = a.AppointmentDateUtc,
            DurationMinutes = a.DurationMinutes,
            EndsAtUtc = a.EndsAtUtc,
            Reason = a.Reason,
            Status = a.Status.ToString(),
            PatientNotes = a.PatientNotes,
            CancellationReason = a.CancellationReason,
            Source = a.Source.ToString(),
            Version = a.Version,
            CreatedAtUtc = a.CreatedAtUtc,
            UpdatedAtUtc = a.UpdatedAtUtc,
        };

    private enum StaffScopeMode
    {
        Clinic,
        Organization,
        Platform,
    }

    private sealed class StaffScope
    {
        private StaffScope(StaffScopeMode mode, Guid organizationId, Guid? clinicId)
        {
            Mode = mode;
            OrganizationId = organizationId;
            ClinicId = clinicId;
        }

        public StaffScopeMode Mode { get; }

        public Guid OrganizationId { get; }

        public Guid? ClinicId { get; }

        public static StaffScope ForClinic(Guid organizationId, Guid clinicId) =>
            new(StaffScopeMode.Clinic, organizationId, clinicId);

        public static StaffScope ForOrganization(Guid organizationId, Guid? clinicId) =>
            new(StaffScopeMode.Organization, organizationId, clinicId);

        public static StaffScope ForPlatform(Guid organizationId, Guid clinicId) =>
            new(StaffScopeMode.Platform, organizationId, clinicId);
    }
}
