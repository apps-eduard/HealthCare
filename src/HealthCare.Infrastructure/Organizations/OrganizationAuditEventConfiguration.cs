using HealthCare.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Organizations;

public sealed class OrganizationAuditEventConfiguration : IEntityTypeConfiguration<OrganizationAuditEvent>
{
    public void Configure(EntityTypeBuilder<OrganizationAuditEvent> builder)
    {
        builder.ToTable("OrganizationAuditEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId)
            .IsRequired();

        builder.Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.Action)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.ResultCode)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.ResourceType)
            .HasMaxLength(64);

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(64);

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.ClinicId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.Action, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.ActorUserId, x.OccurredAtUtc });
        builder.HasIndex(x => new { x.OrganizationId, x.CorrelationId });
    }
}
