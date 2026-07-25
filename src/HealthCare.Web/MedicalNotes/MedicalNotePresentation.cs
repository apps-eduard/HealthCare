using HealthCare.Web.Auth;
using HealthCare.Web.Services;

namespace HealthCare.Web.MedicalNotes;

public static class MedicalNotePermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.MedicalNotesRead);

    public static bool CanCreate(IPermissionState permissions) =>
        permissions.Has(WebPermissions.MedicalNotesCreate);

    public static bool CanUpdateDraft(IPermissionState permissions) =>
        permissions.Has(WebPermissions.MedicalNotesUpdateDraft);

    public static bool CanSign(IPermissionState permissions) =>
        permissions.Has(WebPermissions.MedicalNotesSign);

    public static bool CanAmend(IPermissionState permissions) =>
        permissions.Has(WebPermissions.MedicalNotesAmend);
}

public static class MedicalNoteProblemMessages
{
    public static bool IsConcurrencyConflict(ApiProblemException ex) =>
        string.Equals(ex.ErrorCode, "medical_note.concurrency_conflict", StringComparison.Ordinal);

    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "medical_note.not_found" => "Medical note was not found.",
            "medical_note.access_denied" => "You do not have permission to access medical notes.",
            "medical_note.clinical_role_required" => "Only clinical staff can access medical notes.",
            "medical_note.invalid_appointment_state" =>
                "Notes can only be created for checked-in, in-progress, or completed appointments.",
            "medical_note.not_draft" => "Only draft notes can be edited.",
            "medical_note.already_signed" => "This note is already signed.",
            "medical_note.author_required" => "Only the note author can perform this action.",
            "medical_note.amendment_requires_signed_note" => "Only signed notes can be amended.",
            "medical_note.content_required" => "Add clinical content before saving.",
            "medical_note.concurrency_conflict" => "This note changed elsewhere. Reload and try again.",
            "medical_note.invalid_note_type" => "The selected note type is invalid.",
            "medical_note.note_type_not_allowed" => "That note type is not allowed for your role.",
            "medical_note.amendment_reason_required" => "An amendment reason is required.",
            _ => ex.StatusCode switch
            {
                401 => "Sign in to manage medical notes.",
                403 => "You do not have permission to manage medical notes.",
                404 => "Medical note was not found.",
                _ => string.IsNullOrWhiteSpace(ex.Title) ? "Unable to manage medical notes." : ex.Title,
            },
        };
    }
}
