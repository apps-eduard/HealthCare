using HealthCare.Application.Authorization;
using HealthCare.Application.MedicalNotes;
using HealthCare.Contracts.MedicalNotes;
using HealthCare.Domain.Appointments;
using HealthCare.Domain.MedicalNotes;
using HealthCare.Domain.Patients;
using HealthCare.Domain.Staff;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.MedicalNotes;

public sealed class MedicalNoteService : IMedicalNoteService
{
    private readonly HealthCareDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentStaff _currentStaff;
    private readonly IMedicalNoteAccessService _access;
    private readonly IMedicalNoteAuditStore _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<MedicalNoteService> _logger;

    public MedicalNoteService(
        HealthCareDbContext dbContext,
        ICurrentUser currentUser,
        ICurrentStaff currentStaff,
        IMedicalNoteAccessService access,
        IMedicalNoteAuditStore audit,
        TimeProvider time,
        ILogger<MedicalNoteService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _currentStaff = currentStaff;
        _access = access;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MedicalNoteSummaryResponse>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _access.EnsureClinicalStaffForNotes();
            var appointment = await LoadInScopeAppointmentAsync(appointmentId, cancellationToken);
            var notes = await _dbContext.MedicalNotes
                .AsNoTracking()
                .Where(n => n.AppointmentId == appointment.Id)
                .OrderBy(n => n.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var authorIds = notes.Select(n => n.AuthorStaffMemberId).Distinct().ToList();
            var authors = await _dbContext.StaffMembers
                .AsNoTracking()
                .Where(s => authorIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken);

            await _audit.WriteAsync(
                "medical_note.list",
                "ok",
                appointmentId: appointment.Id,
                patientId: appointment.PatientId,
                clinicId: appointment.ClinicId,
                organizationId: appointment.OrganizationId,
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return notes.Select(n => MapSummary(n, ResolveDisplayName(authors, n.AuthorStaffMemberId))).ToList();
        }
        catch (MedicalNoteException ex)
        {
            await TryAuditDenialAsync("medical_note.list", ex, appointmentId: appointmentId, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<MedicalNoteDetailResponse> GetByIdAsync(
        Guid medicalNoteId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _access.EnsureClinicalStaffForNotes();
            var note = await LoadInScopeNoteAsync(medicalNoteId, cancellationToken);
            var author = await _dbContext.StaffMembers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == note.AuthorStaffMemberId, cancellationToken);

            await _audit.WriteAsync(
                "medical_note.detail",
                "ok",
                medicalNoteId: note.Id,
                appointmentId: note.AppointmentId,
                patientId: note.PatientId,
                clinicId: note.ClinicId,
                organizationId: note.OrganizationId,
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return MapDetail(note, ResolveDisplayName(author));
        }
        catch (MedicalNoteException ex)
        {
            await TryAuditDenialAsync("medical_note.detail", ex, medicalNoteId: medicalNoteId, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<MedicalNoteDetailResponse> CreateDraftAsync(
        Guid appointmentId,
        CreateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _access.EnsureClinicalStaffForNotes();
            if (!MedicalNoteContentValidator.TryParseNoteType(request.NoteType, out var noteType))
            {
                throw MedicalNoteException.InvalidNoteType();
            }

            _access.EnsureNoteTypeAllowed(noteType, _currentStaff.Role);

            var subjective = MedicalNoteRules.NormalizeContent(request.Subjective);
            var objective = MedicalNoteRules.NormalizeContent(request.Objective);
            var assessment = MedicalNoteRules.NormalizeContent(request.Assessment);
            var plan = MedicalNoteRules.NormalizeContent(request.Plan);
            var additional = MedicalNoteRules.NormalizeContent(request.AdditionalText);
            MedicalNoteContentValidator.EnsureFieldLengths(subjective, objective, assessment, plan, additional);
            MedicalNoteContentValidator.EnsureMeaningfulContent(subjective, objective, assessment, plan, additional);

            var appointment = await LoadInScopeAppointmentAsync(appointmentId, cancellationToken);
            if (!MedicalNoteRules.IsEligibleForDraftCreate(appointment.Status))
            {
                throw MedicalNoteException.InvalidAppointmentState();
            }

            EnsureClinicPatientExists(appointment);

            await using var tx = await BeginTransactionIfRelationalAsync(cancellationToken);
            var now = _time.GetUtcNow();
            var note = new MedicalNote
            {
                Id = Guid.NewGuid(),
                OrganizationId = appointment.OrganizationId,
                ClinicId = appointment.ClinicId,
                PatientId = appointment.PatientId,
                ClinicPatientId = appointment.ClinicPatientId,
                AppointmentId = appointment.Id,
                AuthorStaffMemberId = _currentStaff.StaffMemberId,
                AuthorUserId = _currentUser.UserId!.Value,
                NoteType = noteType,
                Status = MedicalNoteStatus.Draft,
                Subjective = subjective,
                Objective = objective,
                Assessment = assessment,
                Plan = plan,
                AdditionalText = additional,
                Version = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            _dbContext.MedicalNotes.Add(note);
            await _audit.WriteAsync(
                "medical_note.create_draft",
                "ok",
                medicalNoteId: note.Id,
                appointmentId: note.AppointmentId,
                patientId: note.PatientId,
                clinicId: note.ClinicId,
                organizationId: note.OrganizationId,
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            var author = await _dbContext.StaffMembers.AsNoTracking()
                .FirstAsync(s => s.Id == note.AuthorStaffMemberId, cancellationToken);
            return MapDetail(note, ResolveDisplayName(author));
        }
        catch (MedicalNoteException ex)
        {
            await TryAuditDenialAsync("medical_note.create_draft", ex, appointmentId: appointmentId, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<MedicalNoteDetailResponse> UpdateDraftAsync(
        Guid medicalNoteId,
        UpdateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _access.EnsureClinicalStaffForNotes();
            var note = await LoadInScopeNoteForUpdateAsync(medicalNoteId, cancellationToken);
            if (note.Status != MedicalNoteStatus.Draft)
            {
                throw MedicalNoteException.NotDraft();
            }

            if (note.AuthorStaffMemberId != _currentStaff.StaffMemberId)
            {
                throw MedicalNoteException.AuthorRequired();
            }

            if (note.Version != request.ExpectedVersion)
            {
                throw MedicalNoteException.ConcurrencyConflict();
            }

            if (!string.IsNullOrWhiteSpace(request.NoteType))
            {
                if (!MedicalNoteContentValidator.TryParseNoteType(request.NoteType, out var noteType))
                {
                    throw MedicalNoteException.InvalidNoteType();
                }

                _access.EnsureNoteTypeAllowed(noteType, _currentStaff.Role);
                note.NoteType = noteType;
            }

            ApplyOptionalField(request.Subjective, request.ClearSubjective, v => note.Subjective = v);
            ApplyOptionalField(request.Objective, request.ClearObjective, v => note.Objective = v);
            ApplyOptionalField(request.Assessment, request.ClearAssessment, v => note.Assessment = v);
            ApplyOptionalField(request.Plan, request.ClearPlan, v => note.Plan = v);
            ApplyOptionalField(request.AdditionalText, request.ClearAdditionalText, v => note.AdditionalText = v);

            MedicalNoteContentValidator.EnsureFieldLengths(
                note.Subjective, note.Objective, note.Assessment, note.Plan, note.AdditionalText);
            MedicalNoteContentValidator.EnsureMeaningfulContent(
                note.Subjective, note.Objective, note.Assessment, note.Plan, note.AdditionalText);

            note.Version++;
            note.UpdatedAtUtc = _time.GetUtcNow();

            await using var tx = await BeginTransactionIfRelationalAsync(cancellationToken);
            await _audit.WriteAsync(
                "medical_note.update_draft",
                "ok",
                medicalNoteId: note.Id,
                appointmentId: note.AppointmentId,
                patientId: note.PatientId,
                clinicId: note.ClinicId,
                organizationId: note.OrganizationId,
                cancellationToken: cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw MedicalNoteException.ConcurrencyConflict();
            }

            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            var author = await _dbContext.StaffMembers.AsNoTracking()
                .FirstAsync(s => s.Id == note.AuthorStaffMemberId, cancellationToken);
            return MapDetail(note, ResolveDisplayName(author));
        }
        catch (MedicalNoteException ex)
        {
            await TryAuditDenialAsync("medical_note.update_draft", ex, medicalNoteId: medicalNoteId, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<MedicalNoteDetailResponse> SignAsync(
        Guid medicalNoteId,
        SignMedicalNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _access.EnsureClinicalStaffForNotes();
            var note = await LoadInScopeNoteForUpdateAsync(medicalNoteId, cancellationToken);
            if (note.Status != MedicalNoteStatus.Draft)
            {
                throw MedicalNoteException.AlreadySigned();
            }

            if (note.AuthorStaffMemberId != _currentStaff.StaffMemberId)
            {
                throw MedicalNoteException.AuthorRequired();
            }

            _access.EnsureNoteTypeAllowed(note.NoteType, _currentStaff.Role);

            if (note.Version != request.ExpectedVersion)
            {
                throw MedicalNoteException.ConcurrencyConflict();
            }

            MedicalNoteContentValidator.EnsureMeaningfulContent(
                note.Subjective, note.Objective, note.Assessment, note.Plan, note.AdditionalText);

            var now = _time.GetUtcNow();
            note.Status = MedicalNoteStatus.Signed;
            note.SignedAtUtc = now;
            note.SignedByStaffMemberId = _currentStaff.StaffMemberId;
            note.Version++;
            note.UpdatedAtUtc = now;

            await using var tx = await BeginTransactionIfRelationalAsync(cancellationToken);
            await _audit.WriteAsync(
                "medical_note.sign",
                "ok",
                medicalNoteId: note.Id,
                appointmentId: note.AppointmentId,
                patientId: note.PatientId,
                clinicId: note.ClinicId,
                organizationId: note.OrganizationId,
                cancellationToken: cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw MedicalNoteException.ConcurrencyConflict();
            }

            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            var author = await _dbContext.StaffMembers.AsNoTracking()
                .FirstAsync(s => s.Id == note.AuthorStaffMemberId, cancellationToken);
            return MapDetail(note, ResolveDisplayName(author));
        }
        catch (MedicalNoteException ex)
        {
            await TryAuditDenialAsync("medical_note.sign", ex, medicalNoteId: medicalNoteId, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<MedicalNoteDetailResponse> AmendAsync(
        Guid medicalNoteId,
        AmendMedicalNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _access.EnsureClinicalStaffForNotes();
            if (!_access.CanAmend(_currentStaff.Role))
            {
                throw MedicalNoteException.AccessDenied();
            }

            var original = await LoadInScopeNoteForUpdateAsync(medicalNoteId, cancellationToken);
            if (original.Status != MedicalNoteStatus.Signed)
            {
                throw MedicalNoteException.AmendmentRequiresSignedNote();
            }

            if (original.Version != request.ExpectedVersion)
            {
                throw MedicalNoteException.ConcurrencyConflict();
            }

            var reason = MedicalNoteRules.NormalizeContent(request.AmendmentReason);
            if (reason is null)
            {
                throw MedicalNoteException.AmendmentReasonRequired();
            }

            MedicalNoteType noteType = original.NoteType;
            if (!string.IsNullOrWhiteSpace(request.NoteType))
            {
                if (!MedicalNoteContentValidator.TryParseNoteType(request.NoteType, out noteType))
                {
                    throw MedicalNoteException.InvalidNoteType();
                }
            }

            _access.EnsureNoteTypeAllowed(noteType, _currentStaff.Role);

            var subjective = request.Subjective is not null
                ? MedicalNoteRules.NormalizeContent(request.Subjective)
                : original.Subjective;
            var objective = request.Objective is not null
                ? MedicalNoteRules.NormalizeContent(request.Objective)
                : original.Objective;
            var assessment = request.Assessment is not null
                ? MedicalNoteRules.NormalizeContent(request.Assessment)
                : original.Assessment;
            var plan = request.Plan is not null
                ? MedicalNoteRules.NormalizeContent(request.Plan)
                : original.Plan;
            var additional = request.AdditionalText is not null
                ? MedicalNoteRules.NormalizeContent(request.AdditionalText)
                : original.AdditionalText;

            MedicalNoteContentValidator.EnsureFieldLengths(
                subjective, objective, assessment, plan, additional, reason);
            MedicalNoteContentValidator.EnsureMeaningfulContent(
                subjective, objective, assessment, plan, additional);

            var now = _time.GetUtcNow();
            var amendment = new MedicalNote
            {
                Id = Guid.NewGuid(),
                OrganizationId = original.OrganizationId,
                ClinicId = original.ClinicId,
                PatientId = original.PatientId,
                ClinicPatientId = original.ClinicPatientId,
                AppointmentId = original.AppointmentId,
                AuthorStaffMemberId = _currentStaff.StaffMemberId,
                AuthorUserId = _currentUser.UserId!.Value,
                NoteType = noteType,
                Status = MedicalNoteStatus.Signed,
                Subjective = subjective,
                Objective = objective,
                Assessment = assessment,
                Plan = plan,
                AdditionalText = additional,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                SignedAtUtc = now,
                SignedByStaffMemberId = _currentStaff.StaffMemberId,
                Version = 0,
                AmendsMedicalNoteId = original.Id,
                AmendmentReason = reason,
            };

            // Concurrency token bump only — clinical fields of the original remain unchanged.
            var originalSubjective = original.Subjective;
            var originalObjective = original.Objective;
            var originalAssessment = original.Assessment;
            var originalPlan = original.Plan;
            var originalAdditional = original.AdditionalText;
            original.Version++;
            original.UpdatedAtUtc = now;

            await using var tx = await BeginTransactionIfRelationalAsync(cancellationToken);
            _dbContext.MedicalNotes.Add(amendment);
            await _audit.WriteAsync(
                "medical_note.amend",
                "ok",
                medicalNoteId: amendment.Id,
                appointmentId: amendment.AppointmentId,
                patientId: amendment.PatientId,
                clinicId: amendment.ClinicId,
                organizationId: amendment.OrganizationId,
                cancellationToken: cancellationToken);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw MedicalNoteException.ConcurrencyConflict();
            }

            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            // Defensive immutability check for unit tests / accidental assignment.
            if (original.Subjective != originalSubjective
                || original.Objective != originalObjective
                || original.Assessment != originalAssessment
                || original.Plan != originalPlan
                || original.AdditionalText != originalAdditional)
            {
                throw MedicalNoteException.AlreadySigned();
            }

            var author = await _dbContext.StaffMembers.AsNoTracking()
                .FirstAsync(s => s.Id == amendment.AuthorStaffMemberId, cancellationToken);
            return MapDetail(amendment, ResolveDisplayName(author));
        }
        catch (MedicalNoteException ex)
        {
            await TryAuditDenialAsync("medical_note.amend", ex, medicalNoteId: medicalNoteId, cancellationToken: cancellationToken);
            throw;
        }
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionIfRelationalAsync(
        CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return null;
        }

        return await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    private async Task<Appointment> LoadInScopeAppointmentAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var appointment = await _dbContext.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);
        if (appointment is null)
        {
            throw MedicalNoteException.NotFound();
        }

        _access.EnsureClinicScope(appointment.OrganizationId, appointment.ClinicId);
        return appointment;
    }

    private async Task<MedicalNote> LoadInScopeNoteAsync(Guid medicalNoteId, CancellationToken cancellationToken)
    {
        var note = await _dbContext.MedicalNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == medicalNoteId, cancellationToken);
        if (note is null)
        {
            throw MedicalNoteException.NotFound();
        }

        _access.EnsureClinicScope(note.OrganizationId, note.ClinicId);
        return note;
    }

    private async Task<MedicalNote> LoadInScopeNoteForUpdateAsync(Guid medicalNoteId, CancellationToken cancellationToken)
    {
        var note = await _dbContext.MedicalNotes
            .FirstOrDefaultAsync(n => n.Id == medicalNoteId, cancellationToken);
        if (note is null)
        {
            throw MedicalNoteException.NotFound();
        }

        _access.EnsureClinicScope(note.OrganizationId, note.ClinicId);
        return note;
    }

    private void EnsureClinicPatientExists(Appointment appointment)
    {
        var exists = _dbContext.ClinicPatients.Any(cp =>
            cp.Id == appointment.ClinicPatientId
            && cp.ClinicId == appointment.ClinicId
            && cp.PatientId == appointment.PatientId);
        if (!exists)
        {
            throw MedicalNoteException.NotFound();
        }
    }

    private async Task TryAuditDenialAsync(
        string operation,
        MedicalNoteException ex,
        Guid? medicalNoteId = null,
        Guid? appointmentId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _audit.WriteAsync(
                operation,
                ex.ErrorCode,
                medicalNoteId: medicalNoteId,
                appointmentId: appointmentId,
                clinicId: _currentStaff.HasActiveMembership ? _currentStaff.ClinicId : null,
                organizationId: _currentStaff.HasActiveMembership ? _currentStaff.OrganizationId : null,
                cancellationToken: cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception auditEx)
        {
            _logger.LogWarning(auditEx, "Failed to persist medical-note denial audit for {Operation}", operation);
        }
    }

    private static void ApplyOptionalField(string? value, bool? clear, Action<string?> assign)
    {
        if (clear == true)
        {
            assign(null);
            return;
        }

        if (value is not null)
        {
            assign(MedicalNoteRules.NormalizeContent(value));
        }
    }

    private static string ResolveDisplayName(StaffMember? staff) =>
        staff is null
            ? "Unknown"
            : !string.IsNullOrWhiteSpace(staff.DisplayName)
                ? staff.DisplayName!
                : $"{staff.FirstName} {staff.LastName}".Trim();

    private static string ResolveDisplayName(IReadOnlyDictionary<Guid, StaffMember> authors, Guid id) =>
        authors.TryGetValue(id, out var staff) ? ResolveDisplayName(staff) : "Unknown";

    private static MedicalNoteSummaryResponse MapSummary(MedicalNote note, string authorDisplayName) =>
        new()
        {
            Id = note.Id,
            AppointmentId = note.AppointmentId,
            NoteType = note.NoteType.ToString(),
            Status = note.Status.ToString(),
            AuthorDisplayName = authorDisplayName,
            CreatedAtUtc = note.CreatedAtUtc,
            UpdatedAtUtc = note.UpdatedAtUtc,
            SignedAtUtc = note.SignedAtUtc,
            Version = note.Version,
            IsAmendment = note.AmendsMedicalNoteId is not null,
            AmendsMedicalNoteId = note.AmendsMedicalNoteId,
        };

    private static MedicalNoteDetailResponse MapDetail(MedicalNote note, string authorDisplayName) =>
        new()
        {
            Id = note.Id,
            AppointmentId = note.AppointmentId,
            PatientId = note.PatientId,
            ClinicId = note.ClinicId,
            OrganizationId = note.OrganizationId,
            NoteType = note.NoteType.ToString(),
            Status = note.Status.ToString(),
            AuthorDisplayName = authorDisplayName,
            AuthorStaffMemberId = note.AuthorStaffMemberId,
            Subjective = note.Subjective,
            Objective = note.Objective,
            Assessment = note.Assessment,
            Plan = note.Plan,
            AdditionalText = note.AdditionalText,
            CreatedAtUtc = note.CreatedAtUtc,
            UpdatedAtUtc = note.UpdatedAtUtc,
            SignedAtUtc = note.SignedAtUtc,
            SignedByStaffMemberId = note.SignedByStaffMemberId,
            Version = note.Version,
            IsAmendment = note.AmendsMedicalNoteId is not null,
            AmendsMedicalNoteId = note.AmendsMedicalNoteId,
            AmendmentReason = note.AmendmentReason,
        };
}
