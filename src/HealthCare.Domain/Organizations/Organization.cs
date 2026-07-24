namespace HealthCare.Domain.Organizations;

public sealed class Organization
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;

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

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<Clinics.Clinic> Clinics { get; set; } = new List<Clinics.Clinic>();
}
