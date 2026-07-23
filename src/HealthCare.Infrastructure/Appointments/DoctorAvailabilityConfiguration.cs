using HealthCare.Domain.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Appointments;

public sealed class DoctorAvailabilityConfiguration : IEntityTypeConfiguration<DoctorAvailability>
{
    public void Configure(EntityTypeBuilder<DoctorAvailability> builder)
    {
        builder.ToTable("DoctorAvailabilities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ClinicId).IsRequired();
        builder.Property(x => x.DoctorStaffMemberId).IsRequired();
        builder.Property(x => x.DayOfWeek).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.StartLocalTime).IsRequired();
        builder.Property(x => x.EndLocalTime).IsRequired();
        builder.Property(x => x.SlotDurationMinutes).IsRequired();
        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken().HasDefaultValue(0);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.DoctorStaffMemberId, x.DayOfWeek, x.IsActive });
        builder.HasIndex(x => new { x.ClinicId, x.DoctorStaffMemberId });
        builder.HasIndex(x => x.OrganizationId);

        builder.HasOne<Domain.Staff.StaffMember>()
            .WithMany()
            .HasForeignKey(x => x.DoctorStaffMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Clinics.Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_DoctorAvailabilities_SlotDuration",
                $"\"SlotDurationMinutes\" >= {AvailabilitySlotRules.MinSlotDurationMinutes} AND \"SlotDurationMinutes\" <= {AvailabilitySlotRules.MaxSlotDurationMinutes}");
            t.HasCheckConstraint(
                "CK_DoctorAvailabilities_TimeOrder",
                "\"StartLocalTime\" < \"EndLocalTime\"");
        });
    }
}

public sealed class DoctorAvailabilityExceptionConfiguration
    : IEntityTypeConfiguration<DoctorAvailabilityException>
{
    public void Configure(EntityTypeBuilder<DoctorAvailabilityException> builder)
    {
        builder.ToTable("DoctorAvailabilityExceptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ClinicId).IsRequired();
        builder.Property(x => x.DoctorStaffMemberId).IsRequired();
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.ExceptionType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Reason).HasMaxLength(250);
        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken().HasDefaultValue(0);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.DoctorStaffMemberId, x.Date });
        builder.HasIndex(x => x.ClinicId);
        builder.HasIndex(x => x.OrganizationId);

        builder.HasOne<Domain.Staff.StaffMember>()
            .WithMany()
            .HasForeignKey(x => x.DoctorStaffMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Clinics.Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
