namespace HealthCare.Application.Organizations;

/// <summary>
/// Foundation for organization audit retention. Purge jobs are deferred; value is exposed for visibility.
/// </summary>
public sealed class AuditRetentionOptions
{
    public const string SectionName = "AuditRetention";

    /// <summary>Intended retention window in days for organization audit events.</summary>
    public int RetentionDays { get; set; } = 365;
}
