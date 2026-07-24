namespace HealthCare.Application.Organizations;

/// <summary>
/// Platform default organization capacity limits. Per-organization overrides live on Organization.
/// </summary>
public sealed class OrganizationLimitsOptions
{
    public const string SectionName = "OrganizationLimits";

    public int DefaultMaxClinics { get; set; } = 10;

    public int DefaultMaxStaff { get; set; } = 100;

    /// <summary>Warn when usage reaches this percent of a hard limit (0–100).</summary>
    public int WarningThresholdPercent { get; set; } = 80;
}
