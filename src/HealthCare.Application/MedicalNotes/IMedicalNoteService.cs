using HealthCare.Application.Authorization;
using HealthCare.Contracts.MedicalNotes;
using HealthCare.Domain.MedicalNotes;

namespace HealthCare.Application.MedicalNotes;

public interface IMedicalNoteService
{
    Task<IReadOnlyList<MedicalNoteSummaryResponse>> ListForAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> GetByIdAsync(
        Guid medicalNoteId,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> CreateDraftAsync(
        Guid appointmentId,
        CreateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> UpdateDraftAsync(
        Guid medicalNoteId,
        UpdateMedicalNoteDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> SignAsync(
        Guid medicalNoteId,
        SignMedicalNoteRequest request,
        CancellationToken cancellationToken = default);

    Task<MedicalNoteDetailResponse> AmendAsync(
        Guid medicalNoteId,
        AmendMedicalNoteRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Clinical-role and clinic-scope checks for medical-note content.
/// Permission catalog alone is insufficient for note bodies.
/// </summary>
public interface IMedicalNoteAccessService
{
    bool IsClinicalRole(string? role);

    void EnsureClinicalStaffForNotes();

    void EnsureClinicScope(Guid organizationId, Guid clinicId);

    void EnsureNoteTypeAllowed(MedicalNoteType noteType, string staffRole);

    bool CanAmend(string staffRole);
}

public sealed class MedicalNoteException : Exception
{
    public MedicalNoteException(string errorCode, string title, int statusCode)
        : base(title)
    {
        ErrorCode = errorCode;
        Title = title;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }

    public string Title { get; }

    public int StatusCode { get; }

    public static MedicalNoteException NotFound() =>
        new(MedicalNoteErrorCodes.NotFound, "Medical note was not found.", 404);

    public static MedicalNoteException AccessDenied() =>
        new(MedicalNoteErrorCodes.AccessDenied, "You do not have access to this medical note.", 403);

    public static MedicalNoteException ClinicalRoleRequired() =>
        new(MedicalNoteErrorCodes.ClinicalRoleRequired, "A clinical staff role is required for medical notes.", 403);

    public static MedicalNoteException InvalidAppointmentState() =>
        new(MedicalNoteErrorCodes.InvalidAppointmentState, "Medical notes cannot be created for this appointment status.", 409);

    public static MedicalNoteException NotDraft() =>
        new(MedicalNoteErrorCodes.NotDraft, "Only draft medical notes can be updated.", 409);

    public static MedicalNoteException AlreadySigned() =>
        new(MedicalNoteErrorCodes.AlreadySigned, "The medical note is already signed.", 409);

    public static MedicalNoteException AuthorRequired() =>
        new(MedicalNoteErrorCodes.AuthorRequired, "Only the original author can perform this action.", 403);

    public static MedicalNoteException AmendmentRequiresSignedNote() =>
        new(MedicalNoteErrorCodes.AmendmentRequiresSignedNote, "Amendments require a signed medical note.", 409);

    public static MedicalNoteException ContentRequired() =>
        new(MedicalNoteErrorCodes.ContentRequired, "At least one note content field is required.", 400);

    public static MedicalNoteException ConcurrencyConflict() =>
        new(MedicalNoteErrorCodes.ConcurrencyConflict, "The medical note was modified by another request. Reload and retry.", 409);

    public static MedicalNoteException InvalidNoteType() =>
        new(MedicalNoteErrorCodes.InvalidNoteType, "The note type is invalid.", 400);

    public static MedicalNoteException NoteTypeNotAllowed() =>
        new(MedicalNoteErrorCodes.NoteTypeNotAllowed, "Your clinical role cannot use this note type.", 403);

    public static MedicalNoteException AmendmentReasonRequired() =>
        new(MedicalNoteErrorCodes.AmendmentReasonRequired, "An amendment reason is required.", 400);
}

public static class MedicalNoteContentValidator
{
    public static void EnsureFieldLengths(
        string? subjective,
        string? objective,
        string? assessment,
        string? plan,
        string? additionalText,
        string? amendmentReason = null)
    {
        EnsureMax(subjective, MedicalNoteRules.MaxSoapFieldLength, nameof(subjective));
        EnsureMax(objective, MedicalNoteRules.MaxSoapFieldLength, nameof(objective));
        EnsureMax(assessment, MedicalNoteRules.MaxSoapFieldLength, nameof(assessment));
        EnsureMax(plan, MedicalNoteRules.MaxSoapFieldLength, nameof(plan));
        EnsureMax(additionalText, MedicalNoteRules.MaxSoapFieldLength, nameof(additionalText));
        if (amendmentReason is not null)
        {
            EnsureMax(amendmentReason, MedicalNoteRules.MaxAmendmentReasonLength, nameof(amendmentReason));
        }
    }

    public static void EnsureMeaningfulContent(
        string? subjective,
        string? objective,
        string? assessment,
        string? plan,
        string? additionalText)
    {
        if (!MedicalNoteRules.HasMeaningfulContent(subjective, objective, assessment, plan, additionalText))
        {
            throw MedicalNoteException.ContentRequired();
        }
    }

    public static bool TryParseNoteType(string? value, out MedicalNoteType noteType)
    {
        noteType = default;
        return !string.IsNullOrWhiteSpace(value)
               && Enum.TryParse(value.Trim(), ignoreCase: true, out noteType)
               && Enum.IsDefined(noteType);
    }

    private static void EnsureMax(string? value, int max, string name)
    {
        if (value is not null && value.Length > max)
        {
            throw new MedicalNoteException(
                MedicalNoteErrorCodes.ContentRequired,
                $"{name} exceeds the maximum length of {max} characters.",
                400);
        }
    }
}
