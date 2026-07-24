using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.Operations;

public static class ReminderStatusPresentation
{
    public static readonly IReadOnlyList<string> AllStatuses =
    [
        "Pending", "Processing", "Sent", "Failed", "Cancelled",
    ];

    public static string DisplayLabel(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim();

    public static StatusTone ChipTone(string? status) =>
        status?.Trim() switch
        {
            "Sent" => StatusTone.Success,
            "Failed" => StatusTone.Error,
            "Pending" => StatusTone.Warning,
            "Processing" => StatusTone.Info,
            "Cancelled" => StatusTone.Default,
            _ => StatusTone.Neutral,
        };

    /// <summary>UI hint only — API remains authoritative for retry eligibility.</summary>
    public static bool AppearsRetryable(string? status) =>
        status is "Failed" or "Pending";

    public static string TypeLabel(string? type) =>
        type?.Trim() switch
        {
            "Confirmation" => "Confirmation",
            "Upcoming" => "Upcoming",
            _ => string.IsNullOrWhiteSpace(type) ? "Unknown" : type.Trim(),
        };
}

public static class SummaryRunStatusPresentation
{
    public static readonly IReadOnlyList<string> AllStatuses =
    [
        "Pending", "Processing", "Completed", "Failed",
    ];

    public static string DisplayLabel(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim();

    public static StatusTone ChipTone(string? status) =>
        status?.Trim() switch
        {
            "Completed" => StatusTone.Success,
            "Failed" => StatusTone.Error,
            "Pending" => StatusTone.Warning,
            "Processing" => StatusTone.Info,
            _ => StatusTone.Neutral,
        };

    /// <summary>UI hint only — API remains authoritative for retry eligibility.</summary>
    public static bool AppearsRetryable(string? status) =>
        status is "Failed" or "Pending";
}

public static class OperationsPermissionRules
{
    public static bool CanViewReminders(IPermissionState permissions) =>
        permissions.Has(WebPermissions.RemindersRead);

    public static bool CanRetryReminders(IPermissionState permissions) =>
        permissions.Has(WebPermissions.RemindersRetry);

    public static bool CanViewSummaries(IPermissionState permissions) =>
        permissions.Has(WebPermissions.SummariesRead);

    public static bool CanRetrySummaries(IPermissionState permissions) =>
        permissions.Has(WebPermissions.SummariesRetry);

    public static bool CanViewOperationsHealth(IPermissionState permissions) =>
        CanViewReminders(permissions) || CanViewSummaries(permissions);

    public static bool CanViewAnyOperations(IPermissionState permissions) =>
        CanViewReminders(permissions) || CanViewSummaries(permissions);
}

public static class ReminderProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "appointment.reminder_not_found" =>
                "The reminder was not found or you do not have access.",
            "appointment.reminder_already_sent" =>
                "This reminder was already sent and cannot be retried.",
            "appointment.reminder_not_retryable" =>
                "This reminder cannot be retried in its current state.",
            "appointment.reminder_delivery_failed" =>
                "Reminder delivery failed. You can retry if the reminder is still eligible.",
            "appointment.reminder_invalid_search" =>
                "The reminder search filters are invalid.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            "authz.clinic_access_denied" =>
                "That clinic is outside your organization scope.",
            _ => ex.ToUserMessage(),
        };
    }

    public static bool IsRetryConflict(ApiProblemException ex) =>
        string.Equals(ex.ErrorCode, "appointment.reminder_already_sent", StringComparison.Ordinal)
        || string.Equals(ex.ErrorCode, "appointment.reminder_not_retryable", StringComparison.Ordinal)
        || ex.StatusCode == 409;
}

public static class SummaryProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "appointment.summary_not_found" =>
                "The clinic summary run was not found or you do not have access.",
            "appointment.summary_already_completed" =>
                "This clinic summary already completed and cannot be retried.",
            "appointment.summary_not_retryable" =>
                "This clinic summary cannot be retried in its current state.",
            "appointment.summary_generation_failed" =>
                "Clinic summary generation failed.",
            "appointment.summary_delivery_failed" =>
                "Clinic summary delivery failed. You can retry if the run is still eligible.",
            "appointment.summary_invalid_date" =>
                "The summary date is invalid.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            "authz.clinic_access_denied" =>
                "That clinic is outside your organization scope.",
            _ => ex.ToUserMessage(),
        };
    }

    public static bool IsRetryConflict(ApiProblemException ex) =>
        string.Equals(ex.ErrorCode, "appointment.summary_already_completed", StringComparison.Ordinal)
        || string.Equals(ex.ErrorCode, "appointment.summary_not_retryable", StringComparison.Ordinal)
        || ex.StatusCode == 409;
}
