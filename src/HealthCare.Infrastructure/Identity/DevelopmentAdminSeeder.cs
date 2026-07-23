using HealthCare.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthCare.Infrastructure.Identity;

public interface IDevelopmentAdminSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent Development-only administrator seeder. Password comes from configuration.
/// </summary>
public sealed class DevelopmentAdminSeeder : IDevelopmentAdminSeeder
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DevelopmentAdminOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DevelopmentAdminSeeder> _logger;

    public DevelopmentAdminSeeder(
        UserManager<ApplicationUser> userManager,
        IOptions<DevelopmentAdminOptions> options,
        IHostEnvironment environment,
        ILogger<DevelopmentAdminSeeder> logger)
    {
        _userManager = userManager;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Email) || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogDebug("Development admin seed skipped: email/password not configured");
            return;
        }

        if (_options.Password.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Development admin seed skipped: password placeholder not replaced");
            return;
        }

        var existing = await _userManager.FindByEmailAsync(_options.Email);
        if (existing is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = _options.Email,
            Email = _options.Email,
            EmailConfirmed = true,
            IsActive = true,
        };

        var createResult = await _userManager.CreateAsync(user, _options.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to seed development admin: {errors}");
        }

        var roleResult = await _userManager.AddToRoleAsync(user, AppRoles.PlatformAdmin);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to assign PLATFORM_ADMIN to development admin: {errors}");
        }

        _logger.LogInformation("Seeded development administrator user");
    }
}

public static class DevelopmentAdminSeederExtensions
{
    public static async Task SeedDevelopmentAdminAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentAdminSeeder>();
        await seeder.SeedAsync(cancellationToken);
    }
}
