using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Identity;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class AuthEndpointTests : IAsyncLifetime
{
    private const string AdminEmail = "admin@healthcare.local";
    private const string AdminPassword = "ChangeMe_Admin_1!";

    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_auth_test")
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
                builder.UseEnvironment(Environments.Development);
                builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
                builder.UseSetting("Jwt:Issuer", "HealthCare");
                builder.UseSetting("Jwt:Audience", "HealthCare");
                builder.UseSetting("Jwt:SigningKey", "DEV_ONLY_HealthCare_Jwt_Signing_Key_Change_Me_32+");
                builder.UseSetting("Jwt:AccessTokenLifetimeMinutes", "15");
                builder.UseSetting("Jwt:RefreshTokenLifetimeDays", "7");
                builder.UseSetting("DevelopmentSeed:Admin:Email", AdminEmail);
                builder.UseSetting("DevelopmentSeed:Admin:Password", AdminPassword);

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.RemoveAll(typeof(HealthCareDbContext));
                    services.AddDbContext<HealthCareDbContext>(options => options.UseNpgsql(connectionString));
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
    public async Task Login_Returns_Access_And_Refresh_Tokens()
    {
        var response = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = AdminEmail,
            Password = AdminPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().NotBeNullOrWhiteSpace();
        body.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Invalid_Login_Is_Rejected()
    {
        var response = await _client!.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = AdminEmail,
            Password = "Wrong_Password_1!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Returns_New_Pair_And_Old_Token_Cannot_Be_Reused()
    {
        var client = _client!;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = AdminEmail,
            Password = AdminPassword,
        });
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = login!.RefreshToken,
        });
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        refreshed!.RefreshToken.Should().NotBe(login.RefreshToken);

        var reuseResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = login.RefreshToken,
        });
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Family should be fully revoked: even the rotated token becomes unusable after reuse detection.
        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = refreshed.RefreshToken,
        });
        afterReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_Revokes_Refresh_Token()
    {
        var client = _client!;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = AdminEmail,
            Password = AdminPassword,
        });
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        var logoutResponse = await client.PostAsJsonAsync("/api/v1/auth/logout", new LogoutRequest
        {
            RefreshToken = login!.RefreshToken,
        });
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refreshResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = login.RefreshToken,
        });
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
