using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;
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

public sealed class ClinicDirectoryEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_clinic_dir_test")
            .WithUsername("healthcare")
            .WithPassword("healthcare_test")
            .Build();

        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();

        await using var migrateDb = new HealthCareDbContext(
            new DbContextOptionsBuilder<HealthCareDbContext>().UseNpgsql(_connectionString).Options);
        await migrateDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [Fact]
    public async Task Anonymous_Clinic_Directory_Returns_401()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/staff-management/clinics")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Clinic_Directory_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/staff-management/clinics")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Clinic_Admin_Sees_Only_Own_Clinic()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");

        var page = await client.GetFromJsonAsync<PagedResponse<ClinicDirectoryItemResponse>>(
            "/api/v1/staff-management/clinics");
        page.Should().NotBeNull();
        page!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Organization_Admin_Sees_Organization_Clinics()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");

        var page = await client.GetFromJsonAsync<PagedResponse<ClinicDirectoryItemResponse>>(
            "/api/v1/staff-management/clinics");
        page.Should().NotBeNull();
        page!.Items.Count.Should().BeGreaterThanOrEqualTo(2);
        var orgIds = page.Items.Select(i => i.OrganizationId).Distinct().ToList();
        orgIds.Should().HaveCount(1);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        var connectionString = _connectionString;
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(IHostedService));
                    services.RemoveAll(typeof(DbContextOptions<HealthCareDbContext>));
                    services.AddDbContext<HealthCareDbContext>(o => o.UseNpgsql(connectionString));
                });
            });
    }

    private static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password,
        });
        login.EnsureSuccessStatusCode();
        var tokens = await login.Content.ReadFromJsonAsync<AuthTokenResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
    }
}
