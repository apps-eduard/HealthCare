using HealthCare.Application.Identity;
using HealthCare.Domain.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Organizations;

public sealed class OrganizationAuditRecorder : IOrganizationAuditRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrganizationAuditRecorder> _logger;

    public OrganizationAuditRecorder(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<OrganizationAuditRecorder> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public void TryRecord(OrganizationAuditWrite write)
    {
        if (write.OrganizationId == Guid.Empty)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HealthCareDbContext>();
            db.OrganizationAuditEvents.Add(new OrganizationAuditEvent
            {
                Id = Guid.NewGuid(),
                OrganizationId = write.OrganizationId,
                ClinicId = write.ClinicId,
                ActorUserId = write.ActorUserId,
                Category = Truncate(write.Category, 64),
                Action = Truncate(write.Action, 128),
                ResultCode = Truncate(write.ResultCode, 64),
                ResourceType = TruncateNullable(write.ResourceType, 64),
                ResourceId = write.ResourceId,
                CorrelationId = TruncateNullable(write.CorrelationId, 64),
                OccurredAtUtc = _timeProvider.GetUtcNow(),
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist organization audit event. Action={Action} OrganizationId={OrganizationId}",
                write.Action,
                write.OrganizationId);
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
