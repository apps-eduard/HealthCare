using HealthCare.Domain.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Appointments;

public sealed class ClinicAppointmentSummaryRunConfiguration
    : IEntityTypeConfiguration<ClinicAppointmentSummaryRun>
{
    public void Configure(EntityTypeBuilder<ClinicAppointmentSummaryRun> builder)
    {
        builder.ToTable("ClinicAppointmentSummaryRuns");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ClinicId).IsRequired();
        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.SummaryDate).IsRequired();
        builder.Property(x => x.ScheduledAtUtc).IsRequired();
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.LastErrorCode).HasMaxLength(80);
        builder.Property(x => x.LastError).HasMaxLength(500);
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(80);
        builder.Property(x => x.BackgroundJobId).HasMaxLength(100);
        builder.Property(x => x.AppointmentCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.ScheduledAtUtc });
        builder.HasIndex(x => new { x.ClinicId, x.SummaryDate });

        builder.HasOne<Domain.Clinics.Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
