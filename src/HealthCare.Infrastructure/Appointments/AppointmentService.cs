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
    private readonly IAuthorizationAuditLogger _audit;
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
        IAuthorizationAuditLogger audit,
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
        _audit = audit;
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
        return await MapAsync(appointment, cancellationToken);
    }

    public async Task<AppointmentResponse> CreateForStaffAsync(
        CreateStaffAppointmentRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var scope = await ResolveStaffClinicScopeAsync(request.ClinicId, bypass, cancellationToken);
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
        return await MapAsync(appointment, cancellationToken);
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
        var appointments = ApplyStaffScope(_dbContext.Appointments.AsNoTracking(), scope);
        appointments = ApplyListFilters(appointments, query);
        var page = await PageAsync(appointments, query, cancellationToken);
        _audit.AppointmentOperation(
            "staff_appointment_list",
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId,
            appointmentId: null);
        return page;
    }

    public async Task<PagedResponse<AppointmentResponse>> ListQueueForStaffAsync(
        AppointmentQueueQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var listQuery = new AppointmentListQuery
        {
            FromUtc = query.FromUtc,
            ToUtc = query.ToUtc,
            Status = query.Status,
            DoctorStaffMemberId = query.DoctorStaffMemberId,
            ClinicId = query.ClinicId,
            Page = query.Page < 1 ? 1 : query.Page,
            PageSize = query.PageSize < 1
                ? AppointmentQueueQueryValidator.DefaultPageSize
                : Math.Min(query.PageSize, AppointmentQueueQueryValidator.MaxPageSize),
            SortBy = "appointmentDateUtc",
            SortDirection = "asc",
        };

        var scope = await ResolveStaffListScopeAsync(listQuery.ClinicId, bypass, cancellationToken);
        var appointments = ApplyStaffScope(_dbContext.Appointments.AsNoTracking(), scope);
        appointments = ApplyListFilters(appointments, listQuery);

        if (string.IsNullOrWhiteSpace(listQuery.Status))
        {
            appointments = appointments.Where(a =>
                a.Status != AppointmentStatus.Completed
                && a.Status != AppointmentStatus.CancelledByPatient
                && a.Status != AppointmentStatus.CancelledByClinic
                && a.Status != AppointmentStatus.NoShow);
        }

        var page = await PageAsync(appointments, listQuery, cancellationToken);
        _audit.AppointmentOperation(
            "staff_appointment_queue",
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId,
            appointmentId: null);
        return page;
    }

    public async Task<PagedResponse<AppointmentResponse>> ListCalendarForStaffAsync(
        AppointmentCalendarQuery query,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var listQuery = new AppointmentListQuery
        {
            FromUtc = query.FromUtc,
            ToUtc = query.ToUtc,
            Status = query.Status,
            DoctorStaffMemberId = query.DoctorStaffMemberId,
            ClinicId = query.ClinicId,
            Page = query.Page < 1 ? 1 : query.Page,
            PageSize = query.PageSize < 1
                ? AppointmentCalendarQueryValidator.DefaultPageSize
                : Math.Min(query.PageSize, AppointmentCalendarQueryValidator.MaxPageSize),
            SortBy = "appointmentDateUtc",
            SortDirection = "asc",
        };

        var scope = await ResolveStaffListScopeAsync(listQuery.ClinicId, bypass, cancellationToken);
        var appointments = ApplyStaffScope(_dbContext.Appointments.AsNoTracking(), scope);
        appointments = ApplyListFilters(appointments, listQuery);
        var page = await PageAsync(appointments, listQuery, cancellationToken);
        _audit.AppointmentOperation(
            "staff_appointment_calendar",
            "succeeded",
            scope.OrganizationId,
            scope.ClinicId,
            appointmentId: null);
        return page;
    }

    public async Task<AppointmentResponse> GetByIdAsync(
        Guid appointmentId,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAccessibleAsync(appointmentId, asNoTracking: true, bypass, cancellationToken);
        return await MapAsync(appointment, cancellationToken);
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

    public async Task<AppointmentResponse> RescheduleAsync(
        Guid appointmentId,
        RescheduleAppointmentRequest request,
        PlatformAdminBypass bypass = PlatformAdminBypass.None,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            throw AuthorizationException.NotAuthenticated();
        }

        var appointment = await LoadAccessibleAsync(appointmentId, asNoTracking: false, bypass, cancellationToken);
        EnsureExpectedVersion(appointment, request.ExpectedVersion);

        if (!AppointmentStatusTransitions.CanReschedule(appointment.Status))
        {
            _logger.LogInformation(
                "Appointment reschedule denied. UserId={UserId} AppointmentId={AppointmentId} Status={Status} Operation={Operation}",
                _currentUser.UserId,
                appointment.Id,
                appointment.Status,
                "appointment_reschedule_denied");
            throw AppointmentException.RescheduleNotAllowed();
        }

        var isPatientActor = _currentUser.IsInRole(AppRoles.Patient) && !_currentStaff.HasActiveMembership;
        if (isPatientActor)
        {
            if (_currentPatient.PatientId != appointment.PatientId)
            {
                LogDenied("appointment_reschedule_patient_denied", appointment.Id);
                throw AppointmentException.NotFoundOrDenied();
            }
        }
        else
        {
            EnsureStaffCanMutate(appointment, bypass);
        }

        var newDoctorId = request.DoctorStaffMemberId is { } doctorId && doctorId != Guid.Empty
            ? doctorId
            : appointment.DoctorStaffMemberId;

        // MVP: remain in the same clinic; client cannot override tenant scope.
        await EnsureAssignableDoctorAsync(newDoctorId, appointment.ClinicId, cancellationToken);
        EnsureFutureStart(request.AppointmentDateUtc);

        if (newDoctorId == appointment.DoctorStaffMemberId
            && request.AppointmentDateUtc == appointment.AppointmentDateUtc
            && request.DurationMinutes == appointment.DurationMinutes)
        {
            _logger.LogInformation(
                "Appointment reschedule same slot. UserId={UserId} AppointmentId={AppointmentId} Operation={Operation}",
                _currentUser.UserId,
                appointment.Id,
                "appointment_reschedule_same_slot");
            throw AppointmentException.RescheduleSameSlot();
        }

        var previousDoctorId = appointment.DoctorStaffMemberId;
        var previousStart = appointment.AppointmentDateUtc;
        var previousDuration = appointment.DurationMinutes;
        var previousVersion = appointment.Version;

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
                newDoctorId,
                request.AppointmentDateUtc,
                request.DurationMinutes,
                excludeAppointmentId: appointment.Id,
                cancellationToken);

            if (await HasSlotConflictAsync(
                    appointment.ClinicId,
                    newDoctorId,
                    request.AppointmentDateUtc,
                    request.DurationMinutes,
                    excludeAppointmentId: appointment.Id,
                    cancellationToken))
            {
                _logger.LogInformation(
                    "Appointment reschedule slot conflict. UserId={UserId} AppointmentId={AppointmentId} ClinicId={ClinicId} DoctorStaffMemberId={DoctorStaffMemberId} Operation={Operation}",
                    _currentUser.UserId,
                    appointment.Id,
                    appointment.ClinicId,
                    newDoctorId,
                    "appointment_reschedule_slot_conflict");
                throw AppointmentException.SlotConflict();
            }

            appointment.DoctorStaffMemberId = newDoctorId;
            appointment.AppointmentDateUtc = request.AppointmentDateUtc;
            appointment.DurationMinutes = request.DurationMinutes;
            appointment.Version++;

            _dbContext.AppointmentRescheduleHistories.Add(new AppointmentRescheduleHistory
            {
                Id = Guid.NewGuid(),
                AppointmentId = appointment.Id,
                PreviousDoctorStaffMemberId = previousDoctorId,
                NewDoctorStaffMemberId = newDoctorId,
                PreviousStartUtc = previousStart,
                NewStartUtc = request.AppointmentDateUtc,
                PreviousDurationMinutes = previousDuration,
                NewDurationMinutes = request.DurationMinutes,
                RescheduledByUserId = _currentUser.UserId!.Value,
                RescheduledAtUtc = _timeProvider.GetUtcNow(),
                Reason = NormalizeOptional(request.Reason),
                PreviousVersion = previousVersion,
            });

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogInformation(
                    "Appointment reschedule concurrency conflict. UserId={UserId} AppointmentId={AppointmentId} Operation={Operation}",
                    _currentUser.UserId,
                    appointment.Id,
                    "appointment_reschedule_concurrency_conflict");
                throw AppointmentException.ConcurrencyConflict();
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (AppointmentException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        catch (AvailabilityException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        catch (AuthorizationException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        catch (Exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Appointment reschedule failed. UserId={UserId} AppointmentId={AppointmentId} Operation={Operation}",
                _currentUser.UserId,
                appointmentId,
                "appointment_reschedule_failed");
            throw AppointmentException.RescheduleFailed();
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        _logger.LogInformation(
            "Appointment rescheduled. UserId={UserId} AppointmentId={AppointmentId} Version={Version} Operation={Operation}",
            _currentUser.UserId,
            appointment.Id,
            appointment.Version,
            "appointment_rescheduled");

        _audit.AppointmentOperation(
            "appointment_rescheduled",
            "succeeded",
            appointment.OrganizationId,
            appointment.ClinicId,
            appointment.Id);

        await _reminders.ScheduleAfterAppointmentRescheduledAsync(appointment.Id, cancellationToken);
        return await MapAsync(appointment, cancellationToken);
    }

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

        _audit.AppointmentOperation(
            operation,
            "succeeded",
            appointment.OrganizationId,
            appointment.ClinicId,
            appointment.Id);

        if (target == AppointmentStatus.Confirmed)
        {
            await _reminders.ScheduleAfterAppointmentConfirmedAsync(appointment.Id, cancellationToken);
        }
        else if (AppointmentStatusTransitions.IsCancelled(target))
        {
            await _reminders.ScheduleAfterAppointmentCancelledAsync(appointment.Id, cancellationToken);
        }

        return await MapAsync(appointment, cancellationToken);
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

        _audit.AppointmentOperation(
            operation,
            "succeeded",
            appointment.OrganizationId,
            appointment.ClinicId,
            appointment.Id);
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
                if (requestClinicId is Guid clinicId && clinicId != Guid.Empty)
                {
                    var clinic = await _dbContext.Clinics
                        .AsNoTracking()
                        .SingleOrDefaultAsync(c => c.Id == clinicId, cancellationToken);
                    if (clinic is null || clinic.OrganizationId != _currentStaff.OrganizationId)
                    {
                        _audit.CrossTenantDenied(
                            "staff_appointment_create_clinic_denied",
                            Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                            _currentStaff.OrganizationId,
                            clinicId);
                        throw AuthorizationException.ClinicAccessDenied();
                    }

                    return StaffScope.ForClinic(_currentStaff.OrganizationId, clinic.Id);
                }

                // Default to assigned membership clinic when no ClinicId supplied.
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
                        _audit.CrossTenantDenied(
                            "staff_appointment_clinic_filter_denied",
                            Contracts.Identity.AuthorizationErrorCodes.ClinicAccessDenied,
                            _currentStaff.OrganizationId,
                            clinicIdFilter);
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

    private static IQueryable<Appointment> ApplyStaffScope(IQueryable<Appointment> appointments, StaffScope scope)
    {
        if (scope.Mode == StaffScopeMode.Clinic || scope.Mode == StaffScopeMode.Platform)
        {
            return appointments.Where(a => a.ClinicId == scope.ClinicId);
        }

        appointments = appointments.Where(a => a.OrganizationId == scope.OrganizationId);
        if (scope.ClinicId.HasValue)
        {
            appointments = appointments.Where(a => a.ClinicId == scope.ClinicId.Value);
        }

        return appointments;
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

    private async Task<PagedResponse<AppointmentResponse>> PageAsync(
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

        var items = await (
                from a in query
                join p in _dbContext.Patients.AsNoTracking() on a.PatientId equals p.Id
                join cp in _dbContext.ClinicPatients.AsNoTracking() on a.ClinicPatientId equals cp.Id
                join d in _dbContext.StaffMembers.AsNoTracking() on a.DoctorStaffMemberId equals d.Id
                join c in _dbContext.Clinics.AsNoTracking() on a.ClinicId equals c.Id
                select new AppointmentResponse
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
                    PatientDisplayName = (p.FirstName + " " + p.LastName).Trim(),
                    LocalPatientNumber = cp.LocalPatientNumber,
                    DoctorDisplayName = d.DisplayName ?? (d.FirstName + " " + d.LastName),
                    ClinicName = c.Name,
                    ClinicSlug = c.Slug,
                    ClinicTimeZoneId = c.TimeZoneId,
                })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return PagedResponse<AppointmentResponse>.Create(items, page, pageSize, totalCount);
    }

    private void LogDenied(string reason, Guid resourceId)
    {
        _audit.CrossTenantDenied(
            reason,
            Contracts.Identity.AuthorizationErrorCodes.Forbidden,
            _currentStaff.HasActiveMembership ? _currentStaff.OrganizationId : null,
            _currentStaff.HasActiveMembership ? _currentStaff.ClinicId : null);
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

    private async Task<AppointmentResponse> MapAsync(Appointment a, CancellationToken cancellationToken)
    {
        var patient = await _dbContext.Patients.AsNoTracking()
            .Where(p => p.Id == a.PatientId)
            .Select(p => new { p.FirstName, p.LastName })
            .SingleOrDefaultAsync(cancellationToken);

        var clinicPatient = await _dbContext.ClinicPatients.AsNoTracking()
            .Where(cp => cp.Id == a.ClinicPatientId)
            .Select(cp => cp.LocalPatientNumber)
            .SingleOrDefaultAsync(cancellationToken);

        var doctor = await _dbContext.StaffMembers.AsNoTracking()
            .Where(d => d.Id == a.DoctorStaffMemberId)
            .Select(d => new { d.DisplayName, d.FirstName, d.LastName })
            .SingleOrDefaultAsync(cancellationToken);

        var clinic = await _dbContext.Clinics.AsNoTracking()
            .Where(c => c.Id == a.ClinicId)
            .Select(c => new { c.Name, c.Slug, c.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken);

        string? doctorName = null;
        if (doctor is not null)
        {
            doctorName = string.IsNullOrWhiteSpace(doctor.DisplayName)
                ? $"{doctor.FirstName} {doctor.LastName}".Trim()
                : doctor.DisplayName;
        }

        return new AppointmentResponse
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
            PatientDisplayName = patient is null ? null : $"{patient.FirstName} {patient.LastName}".Trim(),
            LocalPatientNumber = clinicPatient,
            DoctorDisplayName = doctorName,
            ClinicName = clinic?.Name,
            ClinicSlug = clinic?.Slug,
            ClinicTimeZoneId = clinic?.TimeZoneId,
        };
    }

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
