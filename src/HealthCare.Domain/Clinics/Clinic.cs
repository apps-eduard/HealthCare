using HealthCare.Domain.Organizations;

namespace HealthCare.Domain.Clinics;

public sealed class Clinic
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public required string Name { get; set; }

    public required string Slug { get; set; }

    public string? Specialty { get; set; }

    public string? Description { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Organization? Organization { get; set; }
}
