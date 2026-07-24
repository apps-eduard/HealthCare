namespace HealthCare.Application.Identity;

public interface IOrganizationAuditRecorder
{
    /// <summary>
    /// Best-effort persistence of a safe organization audit event. Failures must not break the caller.
    /// Skips writes when OrganizationId is missing.
    /// </summary>
    void TryRecord(OrganizationAuditWrite write);
}

public sealed class OrganizationAuditWrite
{
    public required Guid OrganizationId { get; init; }

    public Guid? ClinicId { get; init; }

    public Guid? ActorUserId { get; init; }

    public required string Category { get; init; }

    public required string Action { get; init; }

    public required string ResultCode { get; init; }

    public string? ResourceType { get; init; }

    public Guid? ResourceId { get; init; }

    public string? CorrelationId { get; init; }
}
