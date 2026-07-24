namespace HealthCare.Domain.Identity;

/// <summary>
/// Operational security event for organization security summaries.
/// Must never store passwords, tokens, PHI, or full request bodies.
/// </summary>
public enum SecurityEventType
{
    PermissionDenied = 0,
    CrossTenantDenied = 1,
    FailedLogin = 2,
    SessionRevoked = 3,
    CompromisedAccountResponse = 4,
}

public sealed class SecurityEvent
{
    public Guid Id { get; set; }

    public SecurityEventType EventType { get; set; }

    public string Operation { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public Guid? OrganizationId { get; set; }

    public Guid? ClinicId { get; set; }

    public Guid? ActorUserId { get; set; }

    public Guid? TargetUserId { get; set; }

    public Guid? TargetStaffMemberId { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string? CorrelationId { get; set; }
}
