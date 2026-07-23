using FluentAssertions;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthCare.UnitTests;

public sealed class RoleSeederTests
{
    [Fact]
    public async Task SeedAsync_Is_Idempotent_And_Creates_All_Roles()
    {
        await using var provider = BuildServices();
        var seeder = provider.GetRequiredService<IRoleSeeder>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var roles = roleManager.Roles.Select(r => r.Name).ToList();
        roles.Should().BeEquivalentTo(AppRoles.All);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<HealthCareDbContext>(options =>
        {
            options.UseInMemoryDatabase($"roles-{Guid.NewGuid():N}");
        });
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<HealthCareDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IRoleSeeder, RoleSeeder>();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<RoleSeeder>), NullLogger<RoleSeeder>.Instance);

        return services.BuildServiceProvider();
    }
}
