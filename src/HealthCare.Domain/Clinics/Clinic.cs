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

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? Region { get; set; }

    public string? PostalCode { get; set; }

    public string? Country { get; set; }

    /// <summary>
    /// IANA timezone identifier used for local availability windows (e.g. Asia/Riyadh).
    /// Appointment instants are always stored in UTC.
    /// </summary>
    public string TimeZoneId { get; set; } = "Asia/Riyadh";

    public bool IsActive { get; set; } = true;

    /// <summary>Optimistic concurrency token for clinic profile/activation mutations.</summary>
    public int Version { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Organization? Organization { get; set; }
}
