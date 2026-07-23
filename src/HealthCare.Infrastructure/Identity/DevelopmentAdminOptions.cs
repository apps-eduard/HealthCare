namespace HealthCare.Infrastructure.Identity;

public sealed class DevelopmentAdminOptions
{
    public const string SectionName = "DevelopmentSeed:Admin";

    public string? Email { get; set; }

    public string? Password { get; set; }
}
