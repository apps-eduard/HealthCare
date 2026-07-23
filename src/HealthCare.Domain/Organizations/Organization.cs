namespace HealthCare.Domain.Organizations;

public sealed class Organization
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<Clinics.Clinic> Clinics { get; set; } = new List<Clinics.Clinic>();
}
