using HealthCare.Domain.Identity;

namespace HealthCare.Application.Identity;

public interface ISecurityEventRecorder
{
    /// <summary>
    /// Best-effort persistence of a safe security event. Failures must not break the caller.
    /// </summary>
    void TryRecord(SecurityEventWrite write);
}

public sealed class SecurityEventWrite
{
    public required SecurityEventType EventType { get; init; }

    public required string Operation { get; init; }

    public required string ReasonCode { get; init; }

    public Guid? OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    public Guid? ActorUserId { get; init; }

    public Guid? TargetUserId { get; init; }

    public Guid? TargetStaffMemberId { get; init; }

    public string? CorrelationId { get; init; }
}
