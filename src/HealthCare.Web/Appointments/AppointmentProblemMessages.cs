using HealthCare.Web.Services;

namespace HealthCare.Web.Appointments;

public static class AppointmentProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "appointment.not_found" or "appointment.not_found_or_denied" =>
                "Appointment was not found or you do not have access.",
            "appointment.slot_unavailable" =>
                "That time slot is no longer available. Please choose another slot.",
            "appointment.outside_availability" =>
                "The selected time is outside the doctor's availability.",
            "appointment.availability_exception" =>
                "The doctor is unavailable on that date due to an exception.",
            "appointment.invalid_slot_duration" =>
                "The selected duration is not valid for this doctor's schedule.",
            "appointment.invalid_transition" =>
                "This status change is not allowed for the appointment's current state.",
            "appointment.concurrency_conflict" =>
                "This appointment was updated by someone else. Reload and try again.",
            "appointment.reschedule_not_allowed" =>
                "This appointment cannot be rescheduled in its current state.",
            "appointment.slot_conflict" =>
                "The selected slot conflicts with another appointment.",
            "appointment.not_enrolled" =>
                "The patient is not enrolled at this clinic.",
            "appointment.inactive_patient" =>
                "The selected patient is inactive.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            _ => ex.ToUserMessage(),
        };
    }

    public static bool IsConcurrencyConflict(ApiProblemException ex) =>
        string.Equals(ex.ErrorCode, "appointment.concurrency_conflict", StringComparison.Ordinal)
        || (ex.StatusCode == 409
            && (ex.Detail?.Contains("version", StringComparison.OrdinalIgnoreCase) == true
                || ex.Title?.Contains("concurrency", StringComparison.OrdinalIgnoreCase) == true));
}
