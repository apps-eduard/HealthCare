using HealthCare.Application.Identity;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Identity;

public sealed class SecurityEventRecorder : ISecurityEventRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecurityEventRecorder> _logger;

    public SecurityEventRecorder(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SecurityEventRecorder> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void TryRecord(SecurityEventWrite write)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.NewGuid(),
                EventType = write.EventType,
                Operation = Truncate(write.Operation, 128),
                ReasonCode = Truncate(write.ReasonCode, 128),
                OrganizationId = write.OrganizationId,
                ClinicId = write.ClinicId,
                ActorUserId = write.ActorUserId,
                TargetUserId = write.TargetUserId,
                TargetStaffMemberId = write.TargetStaffMemberId,
                OccurredAtUtc = _timeProvider.GetUtcNow(),
                CorrelationId = TruncateNullable(write.CorrelationId, 64),
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist security event. EventType={EventType} Operation={Operation}",
                write.EventType,
                write.Operation);
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }

    private static string? TruncateNullable(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= max ? value : value[..max];
    }
}
