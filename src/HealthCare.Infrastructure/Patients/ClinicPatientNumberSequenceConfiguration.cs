using HealthCare.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Patients;

public sealed class ClinicPatientNumberSequenceConfiguration
    : IEntityTypeConfiguration<ClinicPatientNumberSequence>
{
    public void Configure(EntityTypeBuilder<ClinicPatientNumberSequence> builder)
    {
        builder.ToTable("ClinicPatientNumberSequences");

        builder.HasKey(x => x.ClinicId);

        builder.Property(x => x.LastValue)
            .IsRequired();

        builder.HasOne<Domain.Clinics.Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
