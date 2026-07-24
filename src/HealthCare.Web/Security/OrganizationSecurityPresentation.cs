using HealthCare.Web.Auth;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.Security;

public static class OrganizationSecurityPresentation
{
    public const int MaxInclusiveDays = 93;

    public static StatusTone SessionTone(bool isActive) =>
        isActive ? StatusTone.Success : StatusTone.Default;

    public static string SessionLabel(bool isActive) =>
        isActive ? "Active" : "Inactive";

    public static StatusTone LockoutTone(bool locked) =>
        locked ? StatusTone.Error : StatusTone.Success;

    public static string LockoutLabel(bool locked) =>
        locked ? "Locked out" : "Not locked";

    /// <summary>Privacy-preserving IP display — never show a full address.</summary>
    public static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return "—";
        }

        var value = ip.Trim();
        if (value.Contains(':'))
        {
            var parts = value.Split(':', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "••••" : $"{parts[0]}:••••";
        }

        var octets = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (octets.Length >= 2)
        {
            return $"{octets[0]}.{octets[1]}.*.*";
        }

        return "••••";
    }

    /// <summary>Safe user-agent summary — product family only, no full string.</summary>
    public static string UserAgentSummary(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "—";
        }

        var ua = userAgent.Trim();
        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase))
        {
            return "Edge";
        }

        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
        {
            return "Chrome";
        }

        if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase))
        {
            return "Firefox";
        }

        if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase)
            && !ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase))
        {
            return "Safari";
        }

        if (ua.Contains("HealthCare", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("okhttp", StringComparison.OrdinalIgnoreCase))
        {
            return "App client";
        }

        return "Other client";
    }

    public static string TruncateId(Guid id) =>
        id.ToString("D")[..8] + "…";

    public static string TruncateCorrelation(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return "—";
        }

        var value = correlationId.Trim();
        return value.Length <= 16 ? value : value[..12] + "…";
    }
}

public static class OrganizationSecurityPermissionRules
{
    public static bool CanView(IPermissionState permissions) =>
        permissions.Has(WebPermissions.SecuritySessionsRead);

    public static bool CanRevoke(IPermissionState permissions) =>
        permissions.Has(WebPermissions.SecuritySessionsRevoke);
}

public static class OrganizationSecurityProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "organization_security.access_denied" =>
                "You do not have permission to view organization security data.",
            "organization_security.invalid_scope" =>
                "The selected security scope is invalid.",
            "organization_security.organization_scope_required" =>
                "Select an organization before loading security data.",
            "organization_security.clinic_not_found" =>
                "Clinic was not found or is outside your organization.",
            "organization_security.target_not_found" =>
                "The target staff member was not found.",
            "organization_security.invalid_date_range" =>
                $"Provide From and To (UTC) within {OrganizationSecurityPresentation.MaxInclusiveDays} days.",
            "organization_security.organization_not_found" =>
                "Organization was not found.",
            "organization_security.platform_admin_protected" =>
                "Platform Admin accounts cannot be targeted.",
            "organization_security.last_admin_protected" =>
                "The last Organization Admin cannot be deactivated.",
            "organization_security.self_compromise_denied" =>
                "You cannot run compromise response on your own account.",
            "organization_security.already_inactive" =>
                "The staff account is already inactive or was updated elsewhere. Reload and try again.",
            "staff.not_found" =>
                "Staff member was not found.",
            "staff.session_revocation_failed" =>
                "Session revocation failed. Try again.",
            "staff.concurrency_conflict" =>
                "This staff record was updated elsewhere. Reload and try again.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            "authz.clinic_access_denied" =>
                "That clinic is outside your organization scope.",
            _ => ex.ToUserMessage(),
        };
    }

    public static bool IsConcurrencyConflict(ApiProblemException ex) =>
        string.Equals(ex.ErrorCode, "organization_security.already_inactive", StringComparison.Ordinal)
        || string.Equals(ex.ErrorCode, "staff.concurrency_conflict", StringComparison.Ordinal)
        || ex.StatusCode == 409;
}
