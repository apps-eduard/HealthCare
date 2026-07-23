using FluentAssertions;
using HealthCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class PostgreSqlConnectivityTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("healthcare_connectivity")
        .WithUsername("healthcare")
        .WithPassword("healthcare_test")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task DbContext_Can_Connect_And_Apply_Migrations()
    {
        var options = new DbContextOptionsBuilder<HealthCareDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = new HealthCareDbContext(options);

        await db.Database.MigrateAsync();

        var canConnect = await db.Database.CanConnectAsync();
        canConnect.Should().BeTrue();
    }
}
