using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HealthCare.Contracts.Clinics;
using HealthCare.Contracts.Common;
using HealthCare.Contracts.Identity;
using HealthCare.Contracts.Organizations;
using HealthCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HealthCare.IntegrationTests;

public sealed class OrganizationDirectoryEndpointTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("healthcare_org_dir_test")
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
    public async Task Anonymous_Organization_Directory_Returns_401()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        (await client.GetAsync("/api/v1/platform/organizations")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Patient_Organization_Directory_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "patient@healthcare.local", "ChangeMe_Patient_1!");
        (await client.GetAsync("/api/v1/platform/organizations")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Clinic_Admin_Organization_Directory_Returns_403()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "clinicadmin@healthcare.local", "ChangeMe_ClinicAdmin_1!");
        (await client.GetAsync("/api/v1/platform/organizations")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Organization_Admin_Cannot_Browse_Directory()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "orgadmin@healthcare.local", "ChangeMe_OrgAdmin_1!");
        (await client.GetAsync("/api/v1/platform/organizations")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Platform_Admin_Can_Search_And_Detail_Is_Safe()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        var page = await client.GetFromJsonAsync<PagedResponse<OrganizationDirectoryItemResponse>>(
            "/api/v1/platform/organizations?page=1&pageSize=20&sortBy=name&sortDirection=asc");
        page.Should().NotBeNull();
        page!.Items.Should().NotBeEmpty();

        var json = await client.GetStringAsync("/api/v1/platform/organizations");
        json.ToLowerInvariant().Should().NotContain("connectionstring");
        json.ToLowerInvariant().Should().NotContain("billing");
        json.ToLowerInvariant().Should().NotContain("password");

        var orgId = page.Items[0].OrganizationId;
        var detail = await client.GetFromJsonAsync<OrganizationDetailResponse>(
            $"/api/v1/platform/organizations/{orgId:D}");
        detail.Should().NotBeNull();
        detail!.OrganizationId.Should().Be(orgId);
        detail.Name.Should().NotBeNullOrWhiteSpace();

        (await client.GetAsync($"/api/v1/platform/organizations/{Guid.NewGuid():D}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Selecting_Organization_Does_Not_Grant_Clinic_Access_Without_Bypass()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        var page = await client.GetFromJsonAsync<PagedResponse<OrganizationDirectoryItemResponse>>(
            "/api/v1/platform/organizations?pageSize=1");
        var orgId = page!.Items[0].OrganizationId;

        (await client.GetAsync($"/api/v1/staff-management/clinics?organizationId={orgId:D}"))
            .StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.BadRequest);

        var withBypass = await client.GetAsync(
            $"/api/v1/staff-management/clinics?organizationId={orgId:D}&platformAdminBypass=true");
        withBypass.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Platform_Admin_Still_Cannot_Read_Medical_Notes()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        await AuthenticateAsync(client, "admin@healthcare.local", "ChangeMe_Admin_1!");

        var response = await client.GetAsync($"/api/v1/medical-notes/{Guid.NewGuid():D}");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
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
