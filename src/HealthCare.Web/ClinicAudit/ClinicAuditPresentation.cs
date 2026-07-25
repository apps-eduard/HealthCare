using HealthCare.Contracts.Clinics;
using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.ClinicAudit;

public static class ClinicAuditPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.ClinicAuditLogsRead);
}

public static class ClinicAuditPageCopy
{
    public const int MaxInclusiveDays = 93;

    public static string Subtitle(IPermissionState permissions) =>
        permissions.IsClinicAdmin
            ? "Operational audit events for your clinic (read-only — no export)"
            : permissions.IsPlatformAdmin
                ? "Operational audit events for the selected clinic (read-only — no export)"
                : "Clinic operational audit logs";

    public static string ClinicCaption(IPermissionState permissions, string? clinicName) =>
        permissions.IsClinicAdmin
            ? (string.IsNullOrWhiteSpace(clinicName) ? "Your clinic" : clinicName)
            : (clinicName ?? "Selected clinic");
}

public static class ClinicAuditPresentation
{
    public static readonly IReadOnlyList<string> CommonCategories =
    [
        "clinic", "staff", "appointment", "patient", "availability", "reminder", "summary", "report",
    ];

    public static readonly IReadOnlyList<string> CommonActions = ClinicAuditActions.All
        .OrderBy(a => a, StringComparer.Ordinal)
        .ToList();

    public static StatusTone ResultTone(string? resultCode) =>
        resultCode?.Trim() switch
        {
            "succeeded" or "success" or "ok" or "created" => StatusTone.Success,
            "failed" or "denied" or "error" => StatusTone.Error,
            _ => string.IsNullOrWhiteSpace(resultCode) ? StatusTone.Neutral : StatusTone.Warning,
        };

    public static string TruncateId(Guid? id) =>
        id is Guid value ? value.ToString("D")[..8] + "…" : "—";

    public static string TruncateCorrelation(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return "—";
        }

        var value = correlationId.Trim();
        return value.Length <= 18 ? value : value[..14] + "…";
    }

    public static string FormatLocal(DateTimeOffset utc, string timeZoneId)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var local = TimeZoneInfo.ConvertTime(utc, tz);
            return local.ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
                var local = TimeZoneInfo.ConvertTime(utc, tz);
                return local.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return utc.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC";
            }
        }
    }
}

public static class ClinicAuditProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "clinic_audit.invalid_date_range" =>
                $"Choose a valid date range of at most {ClinicAuditPageCopy.MaxInclusiveDays} inclusive days.",
            "clinic_audit.clinic_scope_required" =>
                "Select a clinic before loading clinic audit logs.",
            "clinic_audit.clinic_not_found" =>
                "The clinic was not found or you do not have access.",
            "clinic_audit.invalid_scope" =>
                "That clinic is outside your authorized scope.",
            "clinic_audit.not_found" =>
                "The audit event was not found.",
            "clinic_audit.access_denied" or "authorization.permission_denied" =>
                "You do not have permission to view clinic audit logs.",
            _ => ex.ToUserMessage(),
        };
    }
}
