using HealthCare.Domain.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Appointments;

public sealed class AppointmentRescheduleHistoryConfiguration
    : IEntityTypeConfiguration<AppointmentRescheduleHistory>
{
    public void Configure(EntityTypeBuilder<AppointmentRescheduleHistory> builder)
    {
        builder.ToTable("AppointmentRescheduleHistories");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AppointmentId).IsRequired();
        builder.Property(x => x.PreviousDoctorStaffMemberId).IsRequired();
        builder.Property(x => x.NewDoctorStaffMemberId).IsRequired();
        builder.Property(x => x.PreviousStartUtc).IsRequired();
        builder.Property(x => x.NewStartUtc).IsRequired();
        builder.Property(x => x.PreviousDurationMinutes).IsRequired();
        builder.Property(x => x.NewDurationMinutes).IsRequired();
        builder.Property(x => x.RescheduledByUserId).IsRequired();
        builder.Property(x => x.RescheduledAtUtc).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(250);
        builder.Property(x => x.PreviousVersion).IsRequired();

        builder.HasIndex(x => x.AppointmentId);
        builder.HasIndex(x => x.RescheduledAtUtc);

        builder.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
