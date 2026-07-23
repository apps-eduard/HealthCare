using HealthCare.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HealthCare.Infrastructure.Identity;

public interface IRoleSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent seeder for the documented ASP.NET Core Identity roles.
/// </summary>
public sealed class RoleSeeder : IRoleSeeder
{
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ILogger<RoleSeeder> _logger;

    public RoleSeeder(
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger<RoleSeeder> logger)
    {
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleName in AppRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await _roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var role = new IdentityRole<Guid>
            {
                Id = Guid.NewGuid(),
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant(),
            };

            var result = await _roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"Failed to seed role '{roleName}': {errors}");
            }

            _logger.LogInformation("Seeded Identity role {RoleName}", roleName);
        }
    }
}

public static class RoleSeederExtensions
{
    public static async Task SeedIdentityRolesAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IRoleSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
