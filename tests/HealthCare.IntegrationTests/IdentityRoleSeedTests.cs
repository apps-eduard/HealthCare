using FluentAssertions;
using HealthCare.Domain.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class IdentityRoleSeedTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_identity_test")
            .WithUsername("healthcare")
            .WithPassword("healthcare_test")
            .Build();

        await _postgres.StartAsync();

        var connectionString = _postgres.GetConnectionString();

        var migrateOptions = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using (var migrateDb = new HealthCareDbContext(migrateOptions))
        {
            await migrateDb.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));

                    services.AddDbContext<HealthCareDbContext>(options =>
                    {
                        options.UseNpgsql(connectionString);
                    });
                });
            });

        // Start the host so role seeding runs.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Startup_Seeds_All_Documented_Roles()
    {
        using var scope = _factory!.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        var roles = await roleManager.Roles.Select(r => r.Name).ToListAsync();

        roles.Should().BeEquivalentTo(AppRoles.All);
    }
}
