namespace HealthCare.Contracts.MedicalNotes;

public static class MedicalNoteErrorCodes
{
    public const string NotFound = "medical_note.not_found";
    public const string AccessDenied = "medical_note.access_denied";
    public const string ClinicalRoleRequired = "medical_note.clinical_role_required";
    public const string InvalidAppointmentState = "medical_note.invalid_appointment_state";
    public const string PatientMismatch = "medical_note.patient_mismatch";
    public const string NotDraft = "medical_note.not_draft";
    public const string AlreadySigned = "medical_note.already_signed";
    public const string AuthorRequired = "medical_note.author_required";
    public const string AmendmentRequiresSignedNote = "medical_note.amendment_requires_signed_note";
    public const string ContentRequired = "medical_note.content_required";
    public const string ConcurrencyConflict = "medical_note.concurrency_conflict";
    public const string InvalidNoteType = "medical_note.invalid_note_type";
    public const string NoteTypeNotAllowed = "medical_note.note_type_not_allowed";
    public const string AmendmentReasonRequired = "medical_note.amendment_reason_required";
}

public sealed class MedicalNoteSummaryResponse
{
    public Guid Id { get; init; }

    public Guid AppointmentId { get; init; }

    public string NoteType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string AuthorDisplayName { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? SignedAtUtc { get; init; }

    public int Version { get; init; }

    public bool IsAmendment { get; init; }

    public Guid? AmendsMedicalNoteId { get; init; }
}

public sealed class MedicalNoteDetailResponse
{
    public Guid Id { get; init; }

    public Guid AppointmentId { get; init; }

    public Guid PatientId { get; init; }

    public Guid ClinicId { get; init; }

    public Guid OrganizationId { get; init; }

    public string NoteType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string AuthorDisplayName { get; init; } = string.Empty;

    public Guid AuthorStaffMemberId { get; init; }

    public string? Subjective { get; init; }

    public string? Objective { get; init; }

    public string? Assessment { get; init; }

    public string? Plan { get; init; }

    public string? AdditionalText { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? SignedAtUtc { get; init; }

    public Guid? SignedByStaffMemberId { get; init; }

    public int Version { get; init; }

    public bool IsAmendment { get; init; }

    public Guid? AmendsMedicalNoteId { get; init; }

    public string? AmendmentReason { get; init; }
}

public sealed class CreateMedicalNoteDraftRequest
{
    public string NoteType { get; init; } = string.Empty;

    public string? Subjective { get; init; }

    public string? Objective { get; init; }

    public string? Assessment { get; init; }

    public string? Plan { get; init; }

    public string? AdditionalText { get; init; }
}

public sealed class UpdateMedicalNoteDraftRequest
{
    public int ExpectedVersion { get; init; }

    public string? NoteType { get; init; }

    public string? Subjective { get; init; }

    public string? Objective { get; init; }

    public string? Assessment { get; init; }

    public string? Plan { get; init; }

    public string? AdditionalText { get; init; }

    public bool? ClearSubjective { get; init; }

    public bool? ClearObjective { get; init; }

    public bool? ClearAssessment { get; init; }

    public bool? ClearPlan { get; init; }

    public bool? ClearAdditionalText { get; init; }
}

public sealed class SignMedicalNoteRequest
{
    public int ExpectedVersion { get; init; }
}

public sealed class AmendMedicalNoteRequest
{
    public int ExpectedVersion { get; init; }

    public string AmendmentReason { get; init; } = string.Empty;

    public string? NoteType { get; init; }

    public string? Subjective { get; init; }

    public string? Objective { get; init; }

    public string? Assessment { get; init; }

    public string? Plan { get; init; }

    public string? AdditionalText { get; init; }
}
