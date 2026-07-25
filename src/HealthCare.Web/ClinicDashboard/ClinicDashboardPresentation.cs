using HealthCare.Contracts.Clinics;
using HealthCare.Web.Services;

namespace HealthCare.Web.ClinicDashboard;

public static class ClinicDashboardProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            ClinicDashboardErrorCodes.AccessDenied =>
                "You do not have permission to view the clinic dashboard.",
            ClinicDashboardErrorCodes.InvalidScope =>
                "The selected clinic dashboard scope is invalid.",
            ClinicDashboardErrorCodes.ClinicScopeRequired =>
                "Select a clinic before loading the clinic dashboard.",
            ClinicDashboardErrorCodes.ClinicNotFound =>
                "Clinic was not found.",
            ClinicDashboardErrorCodes.InvalidDate =>
                "The dashboard date range is invalid.",
            "authorization.permission_denied" =>
                "You do not have permission to view the clinic dashboard.",
            _ => ex.StatusCode switch
            {
                401 => "Sign in to view the clinic dashboard.",
                403 => "You do not have permission to view the clinic dashboard.",
                404 => "Clinic was not found.",
                _ => string.IsNullOrWhiteSpace(ex.Title)
                    ? "Unable to load clinic dashboard."
                    : ex.Title,
            },
        };
    }
}
