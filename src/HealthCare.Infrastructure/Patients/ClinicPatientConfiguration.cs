using HealthCare.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Patients;

public sealed class ClinicPatientConfiguration : IEntityTypeConfiguration<ClinicPatient>
{
    public void Configure(EntityTypeBuilder<ClinicPatient> builder)
    {
        builder.ToTable("ClinicPatients");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ClinicId)
            .IsRequired();

        builder.Property(x => x.PatientId)
            .IsRequired();

        builder.Property(x => x.LocalPatientNumber)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Version)
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        builder.Property(x => x.RegisteredAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.ClinicId, x.PatientId })
            .IsUnique();

        builder.HasIndex(x => new { x.ClinicId, x.LocalPatientNumber })
            .IsUnique();

        builder.HasIndex(x => new { x.ClinicId, x.Status });

        builder.HasIndex(x => x.PatientId);

        builder.HasOne(x => x.Clinic)
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Patient)
            .WithMany(x => x.ClinicPatients)
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
