using HealthCare.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HealthCare.Infrastructure.Identity;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.FamilyId)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedByIp)
            .HasMaxLength(64);

        builder.Property(x => x.CreatedByUserAgent)
            .HasMaxLength(512);

        builder.Property(x => x.RevokedReason)
            .HasMaxLength(128);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique();

        builder.HasIndex(x => x.UserId);

        builder.HasIndex(x => x.FamilyId);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
