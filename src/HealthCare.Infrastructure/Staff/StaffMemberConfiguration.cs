using HealthCare.Domain.Identity;
using HealthCare.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Staff;

public sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        builder.ToTable("StaffMembers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.OrganizationId)
            .IsRequired();

        builder.Property(x => x.ClinicId)
            .IsRequired();

        builder.Property(x => x.Role)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.JobTitle)
            .HasMaxLength(150);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.UserId)
            .IsUnique();

        builder.HasIndex(x => x.ClinicId);

        builder.HasIndex(x => x.OrganizationId);

        builder.HasIndex(x => new { x.ClinicId, x.IsActive });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Organization)
            .WithMany()
            .HasForeignKey(x => x.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Clinic)
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Restrict);

        // Defense in depth: staff role must be one of the documented Identity roles (excluding PATIENT for staff profiles).
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_StaffMembers_Role",
            $"\"Role\" IN ('{AppRoles.PlatformAdmin}', '{AppRoles.OrganizationAdmin}', '{AppRoles.ClinicAdmin}', '{AppRoles.Doctor}', '{AppRoles.Nurse}', '{AppRoles.Receptionist}')"));
    }
}
