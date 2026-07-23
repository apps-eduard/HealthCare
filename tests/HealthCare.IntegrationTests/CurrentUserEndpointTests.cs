using System.Net;
using System.Net.Http.Headers;
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

public sealed class CurrentUserEndpointTests : IAsyncLifetime
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
            .WithDatabase("healthcare_current_user_test")
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
    public async Task Anonymous_Me_Returns_401()
    {
        var response = await _client!.GetAsync("/api/v1/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Authenticated_Me_Returns_Current_User()
    {
        var client = _client!;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = AdminEmail,
            Password = AdminPassword,
        });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var meResponse = await client.GetAsync("/api/v1/auth/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        me.Should().NotBeNull();
        me!.Email.Should().Be(AdminEmail);
        me.Roles.Should().Contain("PLATFORM_ADMIN");
        me.UserId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Client_Supplied_OrganizationId_Does_Not_Grant_Access_For_PlatformAdmin_Without_Bypass()
    {
        var client = _client!;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = AdminEmail,
            Password = AdminPassword,
        });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var foreignOrg = Guid.NewGuid();
        var response = await client.GetAsync($"/api/v1/scope-probe/organization?organizationId={foreignOrg}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PlatformAdmin_Explicit_Bypass_Allows_Cross_Organization_Probe()
    {
        var client = _client!;
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = AdminEmail,
            Password = AdminPassword,
        });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var foreignOrg = Guid.NewGuid();
        var response = await client.GetAsync(
            $"/api/v1/scope-probe/organization?organizationId={foreignOrg}&platformAdminBypass=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
