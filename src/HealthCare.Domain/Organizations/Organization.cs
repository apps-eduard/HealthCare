namespace HealthCare.Domain.Organizations;

public sealed class Organization
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? Country { get; set; }

    /// <summary>
    /// Optional IANA timezone used as the organization operational default (e.g. Asia/Riyadh).
    /// Clinic-level TimeZoneId remains authoritative for clinic scheduling.
    /// </summary>
    public string? DefaultTimeZoneId { get; set; }

    /// <summary>
    /// Optional short branding label/placeholder text — not a logo upload.
    /// </summary>
    public string? BrandingPlaceholder { get; set; }

    /// <summary>
    /// Platform-enforced max clinics. Null uses the configured platform default.
    /// Organization Admin may view but not increase this value.
    /// </summary>
    public int? MaxClinics { get; set; }

    /// <summary>
    /// Platform-enforced max staff memberships. Null uses the configured platform default.
    /// Organization Admin may view but not increase this value.
    /// </summary>
    public int? MaxStaff { get; set; }

    /// <summary>Optimistic concurrency token for organization profile mutations.</summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<Clinics.Clinic> Clinics { get; set; } = new List<Clinics.Clinic>();
}
