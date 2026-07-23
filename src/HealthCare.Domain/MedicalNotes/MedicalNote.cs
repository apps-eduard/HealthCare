using HealthCare.Domain.Appointments;

namespace HealthCare.Domain.MedicalNotes;

public enum MedicalNoteType
{
    Progress = 0,
    Consultation = 1,
    Nursing = 2,
    FollowUp = 3,
    Procedure = 4,
}

public enum MedicalNoteStatus
{
    Draft = 0,
    Signed = 1,
}

/// <summary>
/// Clinical note tied to an appointment. Tenant/patient ownership is server-derived.
/// Signed notes are immutable; corrections use amendment rows linked via <see cref="AmendsMedicalNoteId"/>.
/// </summary>
public sealed class MedicalNote
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid ClinicId { get; set; }

    public Guid PatientId { get; set; }

    public Guid ClinicPatientId { get; set; }

    public Guid AppointmentId { get; set; }

    public Guid AuthorStaffMemberId { get; set; }

    public Guid AuthorUserId { get; set; }

    public MedicalNoteType NoteType { get; set; }

    public MedicalNoteStatus Status { get; set; } = MedicalNoteStatus.Draft;

    public string? Subjective { get; set; }

    public string? Objective { get; set; }

    public string? Assessment { get; set; }

    public string? Plan { get; set; }

    public string? AdditionalText { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? SignedAtUtc { get; set; }

    public Guid? SignedByStaffMemberId { get; set; }

    public int Version { get; set; }

    /// <summary>When set, this row is an amendment of a previously signed note.</summary>
    public Guid? AmendsMedicalNoteId { get; set; }

    public string? AmendmentReason { get; set; }
}

/// <summary>
/// Safe metadata-only audit of medical-note access and lifecycle events.
/// Never store clinical note body fields here.
/// </summary>
public sealed class MedicalNoteAuditEvent
{
    public Guid Id { get; set; }

    public Guid? MedicalNoteId { get; set; }

    public Guid? AppointmentId { get; set; }

    public Guid? PatientId { get; set; }

    public Guid? ClinicId { get; set; }

    public Guid? OrganizationId { get; set; }

    public Guid? ActingUserId { get; set; }

    public Guid? ActingStaffMemberId { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string ResultCode { get; set; } = string.Empty;

    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class MedicalNoteRules
{
    public const int MaxSoapFieldLength = 8000;
    public const int MaxAmendmentReasonLength = 1000;

    public static readonly AppointmentStatus[] EligibleCreateStatuses =
    [
        AppointmentStatus.CheckedIn,
        AppointmentStatus.InProgress,
        AppointmentStatus.Completed,
    ];

    public static bool IsEligibleForDraftCreate(AppointmentStatus status) =>
        EligibleCreateStatuses.Contains(status);

    public static bool HasMeaningfulContent(
        string? subjective,
        string? objective,
        string? assessment,
        string? plan,
        string? additionalText) =>
        HasText(subjective)
        || HasText(objective)
        || HasText(assessment)
        || HasText(plan)
        || HasText(additionalText);

    public static bool HasText(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    public static string? NormalizeContent(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
