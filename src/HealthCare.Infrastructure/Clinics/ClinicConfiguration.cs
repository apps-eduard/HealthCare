using HealthCare.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Clinics;

public sealed class ClinicConfiguration : IEntityTypeConfiguration<Clinic>
{
    public void Configure(EntityTypeBuilder<Clinic> builder)
    {
        builder.ToTable("Clinics");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganizationId)
            .IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Specialty)
            .HasMaxLength(150);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.Address)
            .HasMaxLength(500);

        builder.Property(x => x.City)
            .HasMaxLength(100);

        builder.Property(x => x.PhoneNumber)
            .HasMaxLength(50);

        builder.Property(x => x.Email)
            .HasMaxLength(256);

        builder.Property(x => x.AddressLine1)
            .HasMaxLength(200);

        builder.Property(x => x.AddressLine2)
            .HasMaxLength(200);

        builder.Property(x => x.Region)
            .HasMaxLength(100);

        builder.Property(x => x.PostalCode)
            .HasMaxLength(30);

        builder.Property(x => x.Country)
            .HasMaxLength(100);

        builder.Property(x => x.TimeZoneId)
            .IsRequired()
            .HasMaxLength(64)
            .HasDefaultValue("Asia/Riyadh");

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.Version)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.Slug)
            .IsUnique();

        builder.HasIndex(x => x.OrganizationId);

        builder.HasIndex(x => new { x.OrganizationId, x.IsActive });
    }
}
