using FluentAssertions;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class HealthEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_test")
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

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
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
    public async Task Health_Endpoint_Returns_Success()
    {
        var response = await _client!.GetAsync("/health");

        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    [Fact]
    public async Task Health_Endpoint_Includes_Correlation_Id_Header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", "phase1-test-correlation");

        var response = await _client!.SendAsync(request);

        response.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.Single().Should().Be("phase1-test-correlation");
    }
}
