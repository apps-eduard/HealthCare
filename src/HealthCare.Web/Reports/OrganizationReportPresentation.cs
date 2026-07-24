using HealthCare.Contracts.Organizations;
using HealthCare.Web.Design;
using HealthCare.Web.Services;

namespace HealthCare.Web.Reports;

/// <summary>
/// Catalog and presentation helpers for organization reports.
/// Labels only — aggregates come from the API.
/// </summary>
public static class OrganizationReportCatalog
{
    public const int MaxInclusiveDays = 93;

    public sealed record ReportDefinition(
        string Type,
        string Name,
        string Group,
        string Description,
        bool SupportsDateRange,
        bool SupportsCsv,
        string SupportedFilters);

    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        new(
            OrganizationReportTypes.Appointments,
            "Appointment summary",
            "Appointments",
            "Totals plus appointments by clinic, status, and doctor for the selected date range.",
            SupportsDateRange: true,
            SupportsCsv: true,
            SupportedFilters: "Clinic, FromDate, ToDate"),
        new(
            OrganizationReportTypes.Staff,
            "Staff by clinic",
            "People",
            "Active and inactive staff counts by clinic and role.",
            SupportsDateRange: false,
            SupportsCsv: true,
            SupportedFilters: "Clinic"),
        new(
            OrganizationReportTypes.Patients,
            "Patients by clinic",
            "People",
            "Enrollment counts by clinic. Distinct patients are counted once organization-wide.",
            SupportsDateRange: false,
            SupportsCsv: true,
            SupportedFilters: "Clinic"),
        new(
            OrganizationReportTypes.Availability,
            "Availability coverage",
            "Scheduling",
            "Doctors with active weekly availability windows and exception counts per clinic.",
            SupportsDateRange: false,
            SupportsCsv: true,
            SupportedFilters: "Clinic"),
        new(
            OrganizationReportTypes.ReminderFailures,
            "Reminder failures",
            "Operations",
            "Failed reminder aggregates and a safe failure list (no message bodies or contacts).",
            SupportsDateRange: true,
            SupportsCsv: true,
            SupportedFilters: "Clinic, FromDate, ToDate"),
        new(
            OrganizationReportTypes.SummaryFailures,
            "Clinic-summary failures",
            "Operations",
            "Failed clinic appointment-summary runs and safe failure details.",
            SupportsDateRange: true,
            SupportsCsv: true,
            SupportedFilters: "Clinic, FromDate, ToDate"),
    ];

    public static ReportDefinition? Find(string? type) =>
        All.FirstOrDefault(r => string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase));
}

public static class OrganizationReportPresentation
{
    public static string TimeZoneStrategyLabel(string? strategy) =>
        strategy?.Trim() switch
        {
            "clinic" => "Selected clinic local timezone",
            "per_clinic_local" => "Per-clinic local-date aggregation",
            _ => string.IsNullOrWhiteSpace(strategy) ? "Clinic-local dates (API)" : strategy.Trim(),
        };

    public static string FormatPercent(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return "—";
        }

        var pct = 100.0 * numerator / denominator;
        return $"{pct:0.#}%";
    }

    public static int ProgressPercent(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp(Math.Round(100.0 * numerator / denominator), 0, 100);
    }

    public static StatusTone GapTone(bool hasGap) =>
        hasGap ? StatusTone.Warning : StatusTone.Success;

    public static string GapLabel(bool hasGap) =>
        hasGap ? "Coverage gap" : "Covered";
}

public static class OrganizationReportProblemMessages
{
    public static string ToUserMessage(ApiProblemException ex)
    {
        if (ex.ValidationErrors is { Count: > 0 })
        {
            return string.Join(" ", ex.ValidationErrors.SelectMany(kv => kv.Value));
        }

        return ex.ErrorCode switch
        {
            "organization_reports.access_denied" =>
                "You do not have permission to view organization reports.",
            "organization_reports.invalid_scope" =>
                "The selected report scope is invalid.",
            "organization_reports.organization_scope_required" =>
                "Select an organization before loading reports.",
            "organization_reports.clinic_not_found" =>
                "Clinic was not found or is outside your organization.",
            "organization_reports.invalid_date_range" =>
                $"Provide From and To dates (From ≤ To), at most {OrganizationReportCatalog.MaxInclusiveDays} inclusive days.",
            "organization_reports.organization_not_found" =>
                "Organization was not found.",
            "organization_reports.unknown_report" =>
                "That report type is not supported.",
            "authorization.permission_denied" =>
                "You do not have permission to perform this action.",
            "authz.clinic_access_denied" =>
                "That clinic is outside your organization scope.",
            _ => ex.ToUserMessage(),
        };
    }
}
