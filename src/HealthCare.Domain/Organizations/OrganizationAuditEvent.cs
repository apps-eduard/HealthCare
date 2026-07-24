namespace HealthCare.Domain.Organizations;

/// <summary>
/// Organization-scoped operational audit event for Org Admin query APIs.
/// Must never store passwords, tokens, PHI, medical-note content, or full request bodies.
/// </summary>
public sealed class OrganizationAuditEvent
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public Guid? ClinicId { get; set; }

    public Guid? ActorUserId { get; set; }

    /// <summary>Logical category such as clinic, staff, appointment, security.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Stable operation name (e.g. clinic_created, staff_created).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Succeeded / Failed / or domain result code.</summary>
    public string ResultCode { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public Guid? ResourceId { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}
