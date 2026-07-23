using Microsoft.AspNetCore.Identity;

namespace HealthCare.Domain.Identity;

/// <summary>
/// Global platform identity. UUID primary key; never use email as the primary key.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
