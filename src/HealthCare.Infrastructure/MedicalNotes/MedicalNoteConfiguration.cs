using HealthCare.Domain.MedicalNotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.MedicalNotes;

public sealed class MedicalNoteConfiguration : IEntityTypeConfiguration<MedicalNote>
{
    public void Configure(EntityTypeBuilder<MedicalNote> builder)
    {
        builder.ToTable("MedicalNotes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId).IsRequired();
        builder.Property(x => x.ClinicId).IsRequired();
        builder.Property(x => x.PatientId).IsRequired();
        builder.Property(x => x.ClinicPatientId).IsRequired();
        builder.Property(x => x.AppointmentId).IsRequired();
        builder.Property(x => x.AuthorStaffMemberId).IsRequired();
        builder.Property(x => x.AuthorUserId).IsRequired();

        builder.Property(x => x.NoteType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.Subjective).HasMaxLength(MedicalNoteRules.MaxSoapFieldLength);
        builder.Property(x => x.Objective).HasMaxLength(MedicalNoteRules.MaxSoapFieldLength);
        builder.Property(x => x.Assessment).HasMaxLength(MedicalNoteRules.MaxSoapFieldLength);
        builder.Property(x => x.Plan).HasMaxLength(MedicalNoteRules.MaxSoapFieldLength);
        builder.Property(x => x.AdditionalText).HasMaxLength(MedicalNoteRules.MaxSoapFieldLength);
        builder.Property(x => x.AmendmentReason).HasMaxLength(MedicalNoteRules.MaxAmendmentReasonLength);

        builder.Property(x => x.Version)
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.AppointmentId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.PatientId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ClinicId, x.PatientId });
        builder.HasIndex(x => x.AmendsMedicalNoteId);
        builder.HasIndex(x => x.AuthorStaffMemberId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.OrganizationId);

        builder.HasOne<Domain.Organizations.Organization>()
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Clinics.Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Patients.Patient>()
            .WithMany()
            .HasForeignKey(x => x.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Patients.ClinicPatient>()
            .WithMany()
            .HasForeignKey(x => x.ClinicPatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Appointments.Appointment>()
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Staff.StaffMember>()
            .WithMany()
            .HasForeignKey(x => x.AuthorStaffMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Staff.StaffMember>()
            .WithMany()
            .HasForeignKey(x => x.SignedByStaffMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MedicalNote>()
            .WithMany()
            .HasForeignKey(x => x.AmendsMedicalNoteId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MedicalNoteAuditEventConfiguration : IEntityTypeConfiguration<MedicalNoteAuditEvent>
{
    public void Configure(EntityTypeBuilder<MedicalNoteAuditEvent> builder)
    {
        builder.ToTable("MedicalNoteAuditEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Operation).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ResultCode).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.MedicalNoteId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.AppointmentId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ActingUserId, x.CreatedAtUtc });
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}
