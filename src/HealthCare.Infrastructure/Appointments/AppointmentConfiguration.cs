using HealthCare.Domain.Appointments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Appointments;

public sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ClinicId).IsRequired();
        builder.Property(x => x.PatientId).IsRequired();
        builder.Property(x => x.ClinicPatientId).IsRequired();
        builder.Property(x => x.DoctorStaffMemberId).IsRequired();
        builder.Property(x => x.AppointmentDateUtc).IsRequired();
        builder.Property(x => x.DurationMinutes).IsRequired();

        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.PatientNotes).HasMaxLength(1000);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.CreatedByUserId).IsRequired();

        builder.Property(x => x.Version)
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.Ignore(x => x.EndsAtUtc);

        builder.HasIndex(x => new { x.ClinicId, x.AppointmentDateUtc });
        builder.HasIndex(x => new { x.ClinicId, x.Status });
        builder.HasIndex(x => new { x.PatientId, x.AppointmentDateUtc });
        builder.HasIndex(x => new { x.DoctorStaffMemberId, x.AppointmentDateUtc });
        builder.HasIndex(x => x.OrganizationId);
        builder.HasIndex(x => x.ClinicPatientId);

        builder.HasOne<Domain.Clinics.Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Organizations.Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Patients.Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Patients.ClinicPatient>()
            .WithMany()
            .HasForeignKey(x => x.ClinicPatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Staff.StaffMember>()
            .WithMany()
            .HasForeignKey(x => x.DoctorStaffMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_Appointments_DurationMinutes",
                "\"DurationMinutes\" >= 5 AND \"DurationMinutes\" <= 480");
        });
    }
}
