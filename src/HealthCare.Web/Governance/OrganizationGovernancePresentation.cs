using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.Governance;

public static class OrganizationAuditPresentation
{
    public const int MaxInclusiveDays = 93;

    public static readonly IReadOnlyList<string> CommonCategories =
    [
        "clinic", "staff", "appointment", "patient", "availability", "security", "reminder", "summary", "report",
    ];

    public static StatusTone ResultTone(string? resultCode) =>
        resultCode?.Trim() switch
        {
            "succeeded" or "success" => StatusTone.Success,
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
}

public static class OrganizationUsagePresentation
{
    public static StatusTone LimitTone(bool reached, bool warning) =>
        reached ? StatusTone.Error : warning ? StatusTone.Warning : StatusTone.Success;

    public static string LimitLabel(bool reached, bool warning) =>
        reached ? "Limit reached" : warning ? "Near limit" : "Within capacity";

    public static int CapacityPercent(int used, int max)
    {
        if (max <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(100.0 * used / max), 0, 100);
    }
}

public static class GovernancePermissionRules
{
    public static bool CanViewAuditLogs(IPermissionState permissions) =>
        permissions.Has(WebPermissions.OrganizationAuditLogsRead);

    public static bool CanViewUsage(IPermissionState permissions) =>
        permissions.Has(WebPermissions.OrganizationUsageRead);

    public static bool CanViewAny(IPermissionState permissions) =>
        CanViewAuditLogs(permissions) || CanViewUsage(permissions);
}

public static class OrganizationAuditProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "organization_audit.access_denied" =>
                "You do not have permission to view organization audit logs.",
            "organization_audit.invalid_scope" =>
                "The selected audit scope is invalid.",
            "organization_audit.organization_scope_required" =>
                "Select an organization before loading audit logs.",
            "organization_audit.clinic_not_found" =>
                "Clinic was not found or is outside your organization.",
            "organization_audit.not_found" =>
                "The audit event was not found.",
            "organization_audit.invalid_date_range" =>
                $"Provide From and To (UTC) within {OrganizationAuditPresentation.MaxInclusiveDays} days.",
            "organization_audit.organization_not_found" =>
                "Organization was not found.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            "authz.clinic_access_denied" =>
                "That clinic is outside your organization scope.",
            _ => ex.ToUserMessage(),
        };
    }
}

public static class OrganizationUsageProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "organization_usage.access_denied" =>
                "You do not have permission to view organization usage.",
            "organization_usage.invalid_scope" =>
                "The selected usage scope is invalid.",
            "organization_usage.organization_scope_required" =>
                "Select an organization before loading usage.",
            "organization_usage.clinic_not_found" =>
                "Clinic was not found or is outside your organization.",
            "organization_usage.organization_not_found" =>
                "Organization was not found.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            "authz.clinic_access_denied" =>
                "That clinic is outside your organization scope.",
            _ => ex.ToUserMessage(),
        };
    }
}
