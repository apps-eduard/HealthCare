using HealthCare.Contracts.Clinics;
using HealthCare.Web.Services;

namespace HealthCare.Web.Clinics;

public static class ClinicProblemMessages
{
    public static string From(ApiProblemException ex) =>
        ex.ErrorCode switch
        {
            ClinicManagementErrorCodes.ConcurrencyConflict =>
                "Another change was saved first. Reload the clinic and try again.",
            ClinicManagementErrorCodes.DeactivationNotAllowed =>
                ex.Title ?? "This clinic cannot be deactivated (last active clinic is protected).",
            ClinicManagementErrorCodes.LimitReached =>
                "The organization clinic limit has been reached.",
            ClinicManagementErrorCodes.SlugInUse =>
                "That clinic slug is already in use.",
            ClinicManagementErrorCodes.SlugInvalid =>
                "Clinic slug format is invalid. Use lowercase letters, numbers, and hyphens.",
            ClinicManagementErrorCodes.InitialAdminFailed =>
                ex.Title ?? "Initial Clinic Admin could not be created.",
            ClinicManagementErrorCodes.NotFound =>
                "Clinic was not found or is unavailable.",
            ClinicManagementErrorCodes.AccessDenied =>
                "You do not have permission for this clinic action.",
            _ => ex.ToUserMessage(),
        };
}
