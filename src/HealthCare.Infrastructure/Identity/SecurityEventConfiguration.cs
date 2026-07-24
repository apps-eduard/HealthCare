using HealthCare.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Identity;

public sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("SecurityEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType)
            .IsRequired();

        builder.Property(x => x.Operation)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.ReasonCode)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(64);

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.EventType, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.TargetUserId, x.OccurredAtUtc });
    }
}
